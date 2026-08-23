# Releasing

Versions come from commit history. Nobody edits a version number by hand, and
nobody decides "this feels like a minor" at release time — the commits already
decided.

## Versioning

[Semantic Versioning](https://semver.org), tags prefixed `v` (`v0.2.0`).

Cocogitto reads the conventional commits since the last tag and picks the
increment:

| In the range | Increment |
|---|---|
| Any `!` or `BREAKING CHANGE:` | major (minor while `0.x`) |
| Any `feat` | minor |
| Any `fix`, `perf`, `refactor`, `revert` | patch |
| Only `docs`, `test`, `ci`, `build`, `chore`, `style` | none — nothing to release |

While the version is `0.x` the public API may break in a minor release. That
freedom ends at `1.0`.

## The flow

One workflow, one door. `release.yml` is manually dispatched and does
everything:

```
main is ready
     │
     ▼
release.yml  (manual)   job 1  bump:    cog bump → CHANGELOG.md, VersionPrefix,
     │                                  tag vX.Y.Z, GitHub Release with cog's
     │                                  changelog as the notes
     ▼
              job 2  publish: build + test the tag, pack, attach packages
                              to the release, push to NuGet (when enabled),
                              sync the release into Linear
```

The GitHub Release is the *record* of a release, not a trigger: creating one
by hand in the UI or with `gh release create` does not ship anything. Running
the workflow is the only way to publish, which is the point — there is exactly
one path to audit, gate, and reason about. The `production` environment sits on the
publish job only, so a required-reviewer rule gates publishing without gating
the bump, and `dry-run` never touches it.

### Cutting a release

1. Check what would happen, without changing anything:

   Actions → **release** → Run workflow → `increment: auto`, `dry-run: true`.

   The log prints the version cocogitto would pick and the changelog it would
   write. If the version is not what you expected, the commit messages are why.

2. Run it again with `dry-run: false`. The bump job:
   - runs `dotnet build` and `dotnet test` in Release, so a broken tree cannot
     be tagged (`pre_bump_hooks` in `cog.toml`);
   - updates `CHANGELOG.md`;
   - rewrites `<VersionPrefix>` in `Directory.Build.props`, so a local
     `dotnet pack` produces the same version as CI;
   - commits, tags `vX.Y.Z`, pushes both;
   - creates the GitHub Release with cocogitto's changelog as the notes.

3. The publish job then builds the tag, packs, attaches the packages, pushes
   to NuGet (when enabled), and syncs the release into Linear. Check its
   summary: it says whether packages were pushed or only attached.

Override the increment (`patch`, `minor`, `major`) only when the commit history
genuinely misrepresents the change — and prefer fixing the habit that caused it.

### The bump deploy key

The bump job pushes a version commit and a tag directly to `main`, which a
ruleset blocks for the default workflow token. The push instead authenticates
with a repository **deploy key**: the public half is registered with write
access ("release workflow bump push"), the private half lives in the
`BUMP_SSH_KEY` secret, and **"Deploy keys" must be on the ruleset's bypass
list**. A deploy key never expires and belongs to the repository rather than a
person — the reasons it was chosen over a PAT. To rotate: generate a new
`ed25519` pair, replace the deploy key and the secret, done.

### Local previews

```bash
cog bump --dry-run --auto      # what version and changelog the workflow would produce
```

There is deliberately no local shipping path: a local `cog bump` would tag and
push, but nothing publishes from a tag — only the release workflow does, and
it insists on creating the tag itself. Preview locally, ship from Actions.

## NuGet publishing

Currently **off**. The publish job builds and attaches packages to every
release and skips the push, noting so in the job summary.

Pushes use [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
— nuget.org's recommended replacement for long-lived API keys. The workflow
proves its identity to nuget.org with a GitHub OIDC token and receives an API
key that lives only for that job, so there is no stored secret to leak, rotate,
or scope. To turn it on:

1. Reserve the `Occtoo.Sdk` package ID on [nuget.org](https://www.nuget.org)
   (a `0.1.0` prerelease upload if needed).
2. On the nuget.org account that owns the package, add a **Trusted Publishing
   policy**: repository owner/name, workflow file `release.yml`, environment
   `production`.
3. Set the repository variables `NUGET_USER` (that nuget.org account) and
   `NUGET_PUBLISH` to `true`.

The `publish` job runs in the `production` environment, so a required-reviewer rule
there gates every push to nuget.org without touching the workflow — and the
Trusted Publishing policy pins pushes to exactly this repository, workflow and
environment, so a leaked fork or renamed workflow cannot publish.

Pushes use `--skip-duplicate`, so re-running a release is safe. A published
version is immutable: to correct a bad release, ship the next version.

### Linear release sync

When the `LINEAR_ACCESS_KEY` repository secret is set, the publish job runs
[Linear's official release action](https://github.com/linear/linear-release-action),
which creates or updates the release in the pipeline the key belongs to,
attaches the Linear issues referenced by the commits since the previous
release, and links back to the GitHub Release. The secret must be a
**pipeline access key**, generated from the `dotnet-sdk` pipeline's settings
page in Linear — a personal API key is explicitly not accepted. The step is
best-effort: a Linear outage marks the run but never fails a release that
already shipped.

### Package signing

Deliberately not author-signed. Author signing needs a purchased code-signing
certificate and is rare in the .NET OSS ecosystem; nuget.org repository-signs
every package on upload, which is what the default client trust policy checks.
The supply-chain measures this repository does take are the effective ones:
deterministic builds, SourceLink with embedded sources, Central Package
Management with transitive pinning, and Trusted Publishing instead of a
long-lived credential. Revisit author signing only if a consumer's policy
demands it.

## Prereleases

Not currently exposed: the workflow's `increment` input covers
`auto`/`patch`/`minor`/`major` only. Cocogitto itself supports prerelease
bumps (`cog bump --auto --pre alpha.1`), so when a prerelease is actually
needed, add a `pre` input to the workflow and pass it through — NuGet marks
`-alpha`/`-beta` versions prerelease automatically, and the GitHub Release
should be marked prerelease so it does not show as latest.
