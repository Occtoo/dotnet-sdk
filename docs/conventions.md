# Repository conventions

The rules this repository runs on, and the reasoning behind each. Where a
convention is enforced by tooling, that tooling is named — if it isn't enforced
somewhere, treat it as a convention that will eventually drift.

## Layout

```
src/        shipping code. Everything here is packed and published.
tests/      test projects, one per shipping project, named <project>.Tests;
            folders and namespaces mirror the feature layout of the code under test
examples/   runnable samples, compiled by CI so they cannot go stale
smoke/      deployment-shape checks — CI publishes these as native AOT and runs them
bench/      BenchmarkDotNet projects, compiled by CI, run by hand
docs/       contributor and design documentation
```

Folder-level `Directory.Build.props` files give each folder its role, so a new
project inherits the right settings from its location alone:

| File | Sets |
|---|---|
| `Directory.Build.props` | Target framework, language version, nullable, analysis level, version prefix |
| `src/Directory.Build.props` | `IsPackable`, package metadata, SourceLink, symbol packages, XML docs, `IsAotCompatible` |
| `tests/Directory.Build.props` | `IsTestProject`, the whole test stack, `IsPackable=false` |
| `examples/Directory.Build.props` | `OutputType=Exe`, `IsPackable=false` |
| `smoke/Directory.Build.props` | `OutputType=Exe`, `IsPackable=false` |

## Collections

Concrete or read-only collection types everywhere — `IReadOnlyList<T>`,
`IReadOnlyCollection<T>`, `IReadOnlyDictionary<K,V>`, or a concrete `List<T>`/
array — never `IEnumerable<T>`, in public signatures or internal ones. An
`IEnumerable<T>` hides whether enumeration is cheap, repeatable, or already
materialized; a counted, indexable type states it. (Calling BCL APIs that take
`IEnumerable<T>` is fine — the rule is about surfaces this repository declares.)

## Serialization

Every feature owns a source-generated `JsonSerializerContext` with its naming
policy declared once (`Web`/camelCase for Occtoo payloads, snake_case for
OAuth), and plain wire DTOs with no per-property attributes. That is the
default for any new surface — configuration, events, whatever comes next.

A hand-written `JsonConverter` is the exception, justified per payload only
when one of these holds:

- **The payload scales with user data.** The ingest request is
  O(entries × properties); mapping it through a mirror DTO tree measured ~2×
  the CPU and 10× the allocations of writing the model directly (`bench/`).
  A fixed-size configuration call gains nothing — its DTO costs nanoseconds.
- **The shape has no declarative spelling.** `PropertyValue` writes as its
  native JSON type per case; attributes cannot express that.

When a converter is warranted, scope it to the smallest repeating element (one
`SourceEntry`, not the whole body): converter calls are not resumable, so the
serializer can only flush between them, and element granularity keeps the
writer's buffer bounded regardless of batch size.

## Trimming and native AOT

The SDK ships `IsAotCompatible`, which turns on the trim, AOT and single-file
analyzers for every build of `src/` — CI treats their warnings as errors. The
rules that keep the analyzers quiet: JSON only through source-generated
contexts (never reflection-based `JsonSerializer` overloads), no
`Type`-taking converter factories on public options (the generic
`JsonStringEnumConverter<T>` instead of the reflection one), and no dependency
on ICU behaviour (which is why `LanguageCode` validates against an embedded ISO
list rather than `CultureInfo`).

Analyzers only see this repository's code, so `smoke/Occtoo.Sdk.AotSmoke` is
published in CI the way the strictest consumer would publish — `PublishAot`
with `InvariantGlobalization` — and then executed. A dependency update that
starts relying on reflection fails that job even though no code here changed.

Each folder-level file imports the root one explicitly; MSBuild only auto-imports
the *nearest* `Directory.Build.props`, so without that import the root settings
would silently not apply.

## Solution

`Occtoo.Sdk.slnx` — the XML solution format. Every project must be listed in it:
CI builds the solution, so a project missing from it is a project nobody
compiles.

## Target framework

`net10.0`, single-targeted, set once in the root `Directory.Build.props`.

Single-targeting is a deliberate trade. It keeps the code free of `#if` blocks
and polyfill packages, and lets the SDK use current framework primitives
directly. The cost is that consumers on .NET 8 cannot use it. Adding `net8.0`
later is a `<TargetFrameworks>` change plus the conditional code it forces —
revisit it when a real consumer asks, not pre-emptively.

## Package management

Central Package Management, via `Directory.Packages.props`.
`ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled`
are both on, so:

- `PackageReference` never carries a `Version`;
- transitive dependencies are pinned, which makes builds reproducible and
  makes a vulnerable transitive package something you can fix in one place.

A dependency added to `src/` becomes a dependency of every consumer of the SDK,
and a potential version conflict in their app. Keep that set small and boring.
Test-only and example-only dependencies carry no such cost.

## Packaging

One package, `Occtoo.Sdk`, produced from `src/Occtoo.Sdk`. Package metadata is
declared once in `src/Directory.Build.props`, so a second shipping project would
inherit it and only need its own `PackageId` and `Description`.

Every package ships:

- **XML documentation** (`GenerateDocumentationFile`) — IntelliSense is the
  documentation most consumers will actually read.
- **A symbol package** (`.snupkg`) and **SourceLink** — a consumer can step into
  SDK code from their own debugger.
- **Deterministic build output** — the same source produces the same bytes.
- **The repository README**, so the NuGet page is never empty.

`EnablePackageValidation` catches accidental breaking changes to the public
surface. Once `1.0` ships, set `PackageValidationBaselineVersion` to the previous
release so it compares against what consumers actually have.

## Code style

`.editorconfig` is the authority; `dotnet format --verify-no-changes` runs in
CI. Files are UTF-8 without a byte-order mark — a BOM serves no purpose in a
new codebase and shows up as noise in diffs.

Warnings are advisory locally and errors in CI, via
`TreatWarningsAsErrors` conditioned on `ContinuousIntegrationBuild`. This keeps a
work-in-progress build runnable while guaranteeing nothing lands with warnings.
Reproduce CI locally with:

```bash
dotnet build Occtoo.Sdk.slnx -p:ContinuousIntegrationBuild=true
```

Two conventions worth naming because they differ from common templates:

- **No `Async` suffix.** The `Task` return type already says so.
- **No `#region`.** Organise with well-named types and methods.

## Testing

xunit v3 with Shouldly for assertions and NSubstitute for fakes.

- One test project per shipping project, named `<project>.Tests`.
- `InternalsVisibleTo` is granted to the test project, so internals can be
  tested directly without widening the public surface to make code testable.
- Test method names use underscores (`Sdk_targets_net10`). CA1707 is suppressed
  for `tests/` only.
- Network calls in tests go through a stub `HttpMessageHandler`. A unit test
  must never reach a real Occtoo environment.

## CI

| Workflow | Trigger | Does |
|---|---|---|
| `ci.yml` | Push to `main`, PRs, manual | Format check, build, test, pack |
| `conventions.yml` | PRs, including retitling | `cog check` on the PR's commits, `cog verify` on the title |
| `release.yml` | Manual | `cog bump`, tag, GitHub Release, pack, attach packages, push to NuGet, sync to Linear |

Conventions:

- Every workflow declares `permissions` explicitly, starting from
  `contents: read` and widening only where a job needs it.
- Untrusted event data — PR titles most of all — is passed through `env:` and
  quoted in the shell, never interpolated into a `run:` command.
- Actions are pinned to a major version and updated by Renovate, which pins
  them to digests.
- `concurrency` groups cancel superseded PR runs but never cancel a release.

## Dependency updates

Renovate, configured in `renovate.json`:

- Runs Monday mornings, at most 5 PRs open at once, so review stays feasible.
- Uses semantic commit messages, so its PRs pass the conventions check.
- Groups the test stack into one PR — xunit, its runner and the test SDK move
  together or not at all.
- Automerges patch and minor updates of dev-only dependencies once CI is green.
- Requires dashboard approval for any major update, and never automerges an SDK
  pin change in `global.json`.

## Commits and releases

Conventional Commits, enforced by cocogitto. The version is derived from commit
history rather than chosen by hand, which means **a commit message is a release
decision**. See [releasing.md](releasing.md) and the type table in
[CONTRIBUTING.md](../CONTRIBUTING.md#commit-and-pull-request-conventions).

The conventions workflow checks the commits in a pull request; pushes to
`main` are covered by CI's build gates rather than re-linted.
