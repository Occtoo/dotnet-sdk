# Observability

The SDK is observable through the two standard .NET channels — `ILogger` and
`ActivitySource` — and both are pure opt-in: without a logger factory or a
trace listener, neither costs anything.

## Logging

The SDK logs under the `Occtoo` category prefix —
`Occtoo.Authentication` (token acquisition and renewal, device sign-in),
`Occtoo.Http` (the revoked-token retry), `Occtoo.Sources` (batches sent,
accepted with their correlation id, or rejected), `Occtoo.Events` (pages
pulled, stream connections and reconnects, skipped events). With dependency
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

A token acquisition triggered mid-request nests under the operation that needed
it, so a slow ingest that was really a slow token exchange shows up as exactly
that. Failed operations follow the OpenTelemetry conventions: span status
`Error` with the message, and the standard `error.type` attribute carrying the
error's kind (`RateLimitError`, `ValidationError`, ...) for low-cardinality
faceting.
