# Occtoo .NET SDK

<!-- While the repo is private, only GitHub's own badge endpoint can render
     the CI status (shields.io queries the API anonymously and cannot see a
     private repo). When the repo goes public, switch back to the shields.io
     badge — nuget.org's README image allowlist accepts shields but not this
     endpoint:
     https://img.shields.io/github/actions/workflow/status/Occtoo/dotnet-sdk/ci.yml?branch=main&label=ci -->
[![ci](https://github.com/Occtoo/dotnet-sdk/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Occtoo/dotnet-sdk/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Occtoo.Sdk.svg)](https://www.nuget.org/packages/Occtoo.Sdk)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

The official .NET client for [Occtoo](https://www.occtoo.com). One package,
`Occtoo.Sdk`, organized by feature:

- **Authentication** — every Occtoo credential behind one abstraction, with
  token caching and refresh handled for you.
- **Sources** — typed ingest of entries into your sources.
- **Events** — react to changes across your tenant, by pulling pages or
  subscribing to a live stream of [CloudEvents](https://cloudevents.io/), with
  one typed record per event type. Event destinations (webhooks, Azure Service
  Bus, Azure Storage Queues) deliver the same envelope: `CloudEvent.Parse`
  types any of them, and `OcctooWebhook.Verify` checks webhook signatures —
  the part every receiver otherwise hand-rolls.

> [!IMPORTANT]
> **Status: pre-alpha.** Authentication, typed ingest, and events are
> implemented and tested. Expect breaking changes until `1.0`.

## Getting started

```csharp
using Occtoo;
using Occtoo.Authentication;
using Occtoo.Sources;

using var client = new OcctooClient(new OcctooClientOptions
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(clientId),
            Audience = Audience.From(tenantId),
            Scopes = [OcctooScopes.WriteSources],
        },
        ClientSecret.From(clientSecret)),
});

var outcome = await client.Sources
    .IngestEntries(
        SourceId.From("products"),
        [
            SourceEntry.WithId("sku-123")
                .WithLocalizedText("name", "Blue chair", "en")
                .WithDecimal("price", 100.111m)
                .WithList("tags", "summer", "sale"),
        ])
    .Tap(receipt => logger.LogInformation(
        "Accepted {Count} entries, correlation {Id}",
        receipt.AcceptedEntryCount, receipt.CorrelationId.Value))
    .TapError(error => logger.LogWarning("Ingest failed: {Error}", error));
```

And consuming events — the concrete record type *is* the event type, filters
are built fluently and can only express combinations the API accepts (filtering
`card_definition.updated` by `sourceId` is a compile error, not a `400`), and
the live stream reconnects and resumes by itself:

```csharp
using Occtoo.Events;

var filter = EventFilter
    .OfType<SourceEntryEvent>(e => e.WithSource("products"))
    .OrType<SegmentUpdated>(e => e.WithSegment("summer-sale"));

await foreach (var evt in client.Events.Stream(new EventStreamOptions { Filter = filter }))
{
    switch (evt)
    {
        case SourceEntryAdded e:   Sync(e.SourceId, e.EntryKey); break;
        case SourceEntryEvent e:   Log(e.SourceId);              break; // any other source_entry.*
        case UnknownEvent e:       LogUnknown(e.Type, e.Data);   break; // types newer than this SDK
    }
}
```

Every operation returns `Result<T, OcctooError>`
([CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions))
— expected failures come back as values, not exceptions; throwing is reserved
for construction mistakes (invalid options, invalid identifiers), cancellation,
and the genuinely unexpected. The error hierarchy is the decision surface:
`TransientError` (and its `RateLimitError`, `ServerError`, `NetworkError`,
`TimeoutError`) means retry; everything else names what has to change. See
[docs/errors.md](docs/errors.md).

Identifiers and credentials are validated value objects
([Vogen](https://github.com/SteveDunn/Vogen)): `SourceId`, `EntryId`,
`PropertyId`, an ISO-validating `LanguageCode`, and secret types that mask their
`ToString()`.

The SDK is trim- and native-AOT-compatible: JSON is fully source-generated, the
trim/AOT analyzers are on (and are errors in CI), and CI publishes a native AOT
smoke test — trimmed, ICU-less — and runs it on every push.

It is observable through the standard .NET channels: `ILogger` under the
`Occtoo` category (picked up from DI automatically — token lifecycle, accepted
batches with their correlation ids; one `"Occtoo": "Debug"` line traces a
misbehaving integration), and OpenTelemetry spans from the `Occtoo.Sdk`
`ActivitySource` — add
`.AddSource(OcctooTelemetry.ActivitySourceName)` to your tracer for
`ingest {source}` and `authenticate` spans that stay meaningful even with
`HttpClient` instrumentation suppressed. See
[docs/observability.md](docs/observability.md).

Occtoo accepts several kinds of credential, and the SDK covers all of them:

| Credential | For |
|---|---|
| `OcctooCredential.ApiKey(key)` | An organization API key, sent as `x-api-key` |
| `OcctooCredential.ClientCredentials(authority, secret)` | A machine-to-machine application — services, workers, jobs |
| `OcctooCredential.DeviceCode(authority, promptUser)` | An interactive user sign-in for a CLI or desktop app |
| `OcctooCredential.FromDelegate(...)` | A token minted elsewhere — Key Vault, managed identity, on-behalf-of |

The authority defaults to `https://auth.occtoo.com`; override it only when
Occtoo directs you to another environment. Token acquisition, caching
([FusionCache](https://github.com/ZiggyCreatures/FusionCache) — in-memory by
default, any distributed provider by configuration), refresh-before-expiry,
single-flight under concurrency, and recovery from a revoked token are handled
for you. Full guides: [docs/authentication.md](docs/authentication.md) ·
[docs/sources.md](docs/sources.md) · [docs/events.md](docs/events.md).

Runnable samples under [`examples/`](examples), one project per capability:

| Project | Shows |
|---|---|
| [`Occtoo.Sdk.Examples.Auth`](examples/Occtoo.Sdk.Examples.Auth) | Every auth flow, one file each: client credentials, API key, device login |
| [`Occtoo.Sdk.Examples.Sources.Ingest`](examples/Occtoo.Sdk.Examples.Sources.Ingest) | A hosted worker ingesting typed entries periodically — appsettings, DI, SDK log levels |
| [`Occtoo.Sdk.Examples.Events.Pull`](examples/Occtoo.Sdk.Examples.Events.Pull) | A paginated event consumer that persists its cursor and resumes across restarts |
| [`Occtoo.Sdk.Examples.Events.SSE`](examples/Occtoo.Sdk.Examples.Events.SSE) | A live subscription over Server-Sent Events, filtered, with automatic reconnect |

With dependency injection:

```csharp
builder.Services.AddOcctooClient(options => options with
{
    Credential = OcctooCredential.ClientCredentials(authority, secret),
});

// or, for several environments side by side:
builder.Services.AddKeyedOcctooClient("europe", options => options with { ... });
```

## Why this SDK exists

Occtoo already has an OpenAPI contract for its APIs, so generating a client
would be a few minutes' work. This SDK deliberately isn't that. A generated
client hands you the HTTP shape and leaves the hard parts to every caller:
token lifetime, batch limits, retry and backoff, the difference between a
validation error and an outage.

The goal here is that the *correct* integration is also the shortest one to
write. Concretely:

| The API requires | So the SDK does |
|---|---|
| A token valid for 60 minutes, from a rate-limited endpoint | Acquire, cache and refresh tokens for you — never once per request |
| Typed JSON matching each property's configured type | A closed `PropertyValue` union — the invalid shapes cannot be expressed |
| Ids ≤ 256 chars, lowercase property ids, ISO language codes | Value objects that enforce the rules at construction, before the round trip |
| `Retry-After` on `429`, retry only what is retryable | Built-in Polly retries that honor `Retry-After`, configurable via `Resilience` options — plus a typed error hierarchy where `TransientError` *is* the decision for what survives them |
| Errors keyed by request path in problem details | `ValidationError.Failures`, plus `requestId`/`traceId` in every message |
| Do not retry an accepted batch | A receipt type whose docs and shape say exactly that |

See [docs/design-principles.md](docs/design-principles.md) for the reasoning.

## Installation

```bash
dotnet add package Occtoo.Sdk
```

## The APIs being wrapped

All live behind the Occtoo public API host, `https://api.occtoo.com`, with
tokens issued by `https://auth.occtoo.com`.

### Sources (typed ingest)

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/v1/sources/{sourceId}` | Validate typed JSON entries and queue them for asynchronous processing |

Requires the `write:sources` scope. The legacy string-based import
(`/datasources/{dataSource}/import`) and media ingest are not wrapped yet.

### Events

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/v1/events` | Pull a page of events in ascending `sequence` order, by cursor |
| `GET` | `/v1/events/stream` | Subscribe to a resumable Server-Sent Events stream |
| `GET` | `/v1/events/metadata` | Inspect the stream's shape — first/latest position, tail cursor, count — without payloads |

The catalog endpoints (`/v1/event-types`, `/v1/events/schemas/{type}/{version}`)
are not wrapped: the SDK ships the catalog as types — one sealed record per
event type, so the schema is what your code compiles against.

Full reference: [Ingest API](https://docs.occtoo.com/api-reference/ingest/overview)
· [Events API](https://docs.occtoo.com/api-reference/events/overview)

## Repository layout

```
src/        shipping code — everything here is packed into Occtoo.Sdk
tests/      test projects, mirroring src/
examples/   runnable samples, compiled by CI so they cannot go stale
docs/       contributor and design documentation
```

Inside `src/Occtoo.Sdk`, code is organized by feature: `Authentication/`,
`Sources/`, `Events/`, and `Common/` for what they share (the error model, the
HTTP pipeline, JSON conventions).

| File | Role |
|---|---|
| `Occtoo.Sdk.slnx` | Solution, in the XML `slnx` format |
| `global.json` | Pins the .NET SDK |
| `Directory.Build.props` | Properties shared by every project |
| `Directory.Packages.props` | Central Package Management — every version lives here |
| `cog.toml` | Cocogitto: commit conventions, changelog, version bumps |
| `renovate.json` | Dependency update policy |

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build Occtoo.Sdk.slnx
dotnet test Occtoo.Sdk.slnx
dotnet format Occtoo.Sdk.slnx        # apply formatting
```

CI builds with `ContinuousIntegrationBuild=true`, which turns warnings into
errors. Run `dotnet build -p:ContinuousIntegrationBuild=true` to reproduce that
locally before pushing.

## Contributing

Commits and pull request titles follow
[Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/),
enforced by [cocogitto](https://docs.cocogitto.io). Start with
[CONTRIBUTING.md](CONTRIBUTING.md) and
[docs/conventions.md](docs/conventions.md).

## Releasing

Versions are derived from commit history, so what you write in a commit message
determines the next version number. The flow is documented in
[docs/releasing.md](docs/releasing.md).

## License

[MIT](LICENSE)
