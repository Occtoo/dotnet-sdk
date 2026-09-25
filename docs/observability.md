# Observability

The SDK is observable through the two standard .NET channels — `ILogger` and
`ActivitySource` — and both are pure opt-in: without a logger factory or a
trace listener, neither costs anything.

## Logging

The SDK logs under the `Occtoo` category prefix —
`Occtoo.Authentication` (token acquisition and renewal, device sign-in),
`Occtoo.Http` (the revoked-token retry), `Occtoo.Sources` (batches sent,
accepted with their correlation id, or rejected), `Occtoo.Events` (pages
pulled, stream connections and reconnects, skipped events), `Occtoo.Applications`
(applications created and deleted), `Occtoo.ManagedTags` (tags created and
deleted), `Occtoo.Assets` (upload runs and their counts, refused assets,
re-signed links, transfers sent again). With dependency
injection the
host's logging is picked up automatically; without it, set
`OcctooClientOptions.LoggerFactory`.

The levels form a ladder made for operations: `Information` is the rare,
meaningful state changes; `Warning` is failures and recoveries; `Debug`
narrates decisions (token cache misses, why the token endpoint was called
again, device-login polling); `Trace` adds per-request token cache hits. To
trace a misbehaving integration:

```json
"Logging": {
  "LogLevel": {
    "Occtoo": "Debug",
    "System.Net.Http.HttpClient.Occtoo": "Information"
  }
}
```

Every log event carries a stable `EventId`, grouped by feature: `1xx`
authentication, `2xx` http, `3xx` sources, `4xx` events, `5xx` applications,
`6xx` managed tags, `7xx` assets. Filter on those rather than on message text.

The second category is the transport itself — `AddOcctooClient` registers a
named `HttpClient` called `Occtoo`, so the standard `IHttpClientFactory`
request/response logs are there when wire-level detail is needed.

## Tracing (OpenTelemetry)

The SDK emits spans through an `ActivitySource` named `Occtoo.Sdk`, following
the OpenTelemetry guidance for .NET libraries: instrument with
`System.Diagnostics.Activity` only, take no OpenTelemetry package dependency,
and let the application own the tracer. The spans exist only when a listener
subscribes — without one, tracing costs nothing.

Wire it up by adding the source to your tracer
(`OcctooTelemetry.ActivitySourceName` is the constant for the name):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(OcctooTelemetry.ActivitySourceName)   // "Occtoo.Sdk"
        .AddOtlpExporter());
```

Anything else that listens to `ActivitySource` — the Aspire dashboard,
`dotnet-trace`, a hand-rolled `ActivityListener` — sees the same spans.

The spans are logical operations, not HTTP calls — so a trace stays meaningful
when `HttpClient` instrumentation is suppressed, and gains the wire-level child
spans when it is not:

| Span | Kind | Attributes |
|---|---|---|
| `ingest {source}` | Client | `occtoo.source.id`, `occtoo.ingest.entry_count`, `occtoo.ingest.correlation_id` (on acceptance) |
| `authenticate` | Client | `occtoo.credential.type` (`client_credentials`, `device_code`, `delegate`) |
| `pull events` | Client | `occtoo.events.limit`, `occtoo.events.count` |
| `stream events` | Client | one span per connection attempt |
| `events metadata` | Client | `occtoo.events.total` |
| `list sources`, `get source`, `create source`, … | Client | one span per management operation, named after it; `occtoo.source.id`, `occtoo.property.id`, `occtoo.application.id`, `occtoo.page.limit` where they apply |
| `upload assets` | Client | `occtoo.source.id`, `occtoo.assets.count`, `occtoo.assets.completed`, `occtoo.assets.failed` |
| `initialize assets` | Client | `occtoo.source.id`, `occtoo.assets.count`, `occtoo.folder.id` (when given), `occtoo.assets.signed`, `occtoo.assets.refused` |
| `refresh asset upload links` | Client | `occtoo.source.id`, `occtoo.assets.count`, `occtoo.assets.signed`, `occtoo.assets.refused` |
| `transfer asset` | Client | `occtoo.assets.key`, `occtoo.assets.bytes` |
| `complete assets` | Client | `occtoo.source.id`, `occtoo.assets.count`, `occtoo.assets.completed`, `occtoo.assets.failed` |
| `read asset states` | Client | `occtoo.source.id`, `occtoo.assets.count`, `occtoo.assets.found` |
| `delete assets` | Client | `occtoo.source.id`, `occtoo.assets.count` |

A token acquisition triggered mid-request nests under the operation that needed
it, so a slow ingest that was really a slow token exchange shows up as exactly
that. Failed operations follow the OpenTelemetry conventions: span status
`Error` with the message, and the standard `error.type` attribute carrying the
error's kind (`RateLimitError`, `ValidationError`, ...) for low-cardinality
faceting.

The asset key is high cardinality, so it sits only on `transfer asset`, where
one span is one file and the key is what an operator searches for. Both
`Transfer` and `Upload` open that span, so a run under `upload assets` shows one
child per file, retries included.
