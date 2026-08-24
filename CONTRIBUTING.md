# Contributing

Thanks for helping build the Occtoo .NET SDK.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — the exact
  version is pinned in [`global.json`](global.json)
- [cocogitto](https://docs.cocogitto.io) (`brew install cocogitto` or
  `cargo install cocogitto`) — optional locally, but it lets you catch a bad
  commit message before CI does
- [gitleaks](https://github.com/gitleaks/gitleaks) (`brew install gitleaks`) —
  optional locally, but it lets the pre-commit hook catch a leaked secret
  before CI does

## Getting started

```bash
git clone https://github.com/Occtoo/dotnet-sdk.git
cd dotnet-sdk
dotnet build Occtoo.Sdk.slnx
dotnet test Occtoo.Sdk.slnx
```

Install the commit-message hook once, so `git commit` rejects a non-conventional
message locally:

```bash
cog install-hook --all
```

And the secret-scanning pre-commit hook, so a staged secret blocks the commit
instead of reaching the remote (rules and test-fixture allowlists live in
[`.gitleaks.toml`](.gitleaks.toml); CI runs the same scan either way):

```bash
ln -sf ../../.githooks/pre-commit .git/hooks/pre-commit
```

## Making a change

1. Branch from `main`. Name it after the change, e.g. `feat/ingest-batching`.
2. Write the change and its tests together. New public API needs XML doc
   comments — they ship in the package.
3. Run the checks CI runs:

   ```bash
   dotnet format Occtoo.Sdk.slnx --verify-no-changes
   dotnet build Occtoo.Sdk.slnx -p:ContinuousIntegrationBuild=true
   dotnet test Occtoo.Sdk.slnx
   ```

   `ContinuousIntegrationBuild=true` turns warnings into errors, which is what
   CI does. Without it a warning passes locally and fails on the PR.
4. Open a pull request with a
   [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) title.
   PRs are squash-merged, so **the title becomes the changelog entry and decides
   the next version number.**

## Commit and pull request conventions

```
<type>[(scope)][!]: <description>
```

| Type | Use for | Version effect |
|---|---|---|
| `feat` | New capability | minor |
| `fix` | Bug fix | patch |
| `perf` | Performance improvement | patch |
| `refactor` | Restructuring with no behaviour change | patch |
| `docs` | Documentation only | none |
| `test` | Tests only | none |
| `build` | Build system, packaging | none |
| `ci` | Workflows and CI scripts | none |
| `chore` | Maintenance, dependencies | none |
| `style` | Formatting only | none |
| `revert` | Reverting a previous commit | patch |

A `!` after the type or scope, or a `BREAKING CHANGE:` footer, forces a major
bump (a minor bump while the version is still `0.x`).

Use the area touched as the scope: `ingest`, `events`, `auth`, `http`, `deps`,
`release`. Examples:

```
feat(ingest): batch entities to respect the 20 MB payload limit
fix(events): keep the cursor unchanged when a page comes back empty
feat(events)!: rename PullOptions.Limit to PullOptions.PageSize
chore(deps): update xunit.v3 to 3.2.1
docs: explain cursor durability
```

Two checks gate the merge: [cocogitto-bot](https://github.com/apps/cocogitto-bot)
and the `conventions` workflow. Both run the rules in
[`cog.toml`](cog.toml).

## Dependencies

Central Package Management is on. Never put a `Version` on a
`PackageReference`:

1. Add or bump `<PackageVersion Include="..." Version="..." />` in
   [`Directory.Packages.props`](Directory.Packages.props).
2. Reference it from the project as `<PackageReference Include="..." />`.

New dependencies in `src/` are a deliberate decision — every one of them becomes
a transitive dependency for everyone using the SDK. Raise it in the PR
description. Test-only and example-only dependencies are cheap.

## Where things go

| Adding | Goes in |
|---|---|
| Shipping code | `src/Occtoo.Sdk/` |
| Tests | `tests/Occtoo.Sdk.Tests/` |
| A runnable sample | `examples/<name>/` — add it to `Occtoo.Sdk.slnx` so CI compiles it |
| Contributor or design docs | `docs/` |

More detail in [docs/conventions.md](docs/conventions.md).
