# Design principles

Occtoo publishes an OpenAPI contract, so a generated client is a few minutes'
work. This SDK exists because a generated client transcribes the HTTP surface
and leaves the operational knowledge — token lifetimes, rate limits, batch
ceilings, retry semantics — for every integrator to relearn and reimplement.
The SDK's job is to know it once, correctly.

## Principles

- **The obvious call is the correct call.** If the caller must remember an
  ordering constraint to stay correct, that is an SDK design bug, not a
  documentation gap.
- **Push failures earlier.** A validation error the caller can fix locally
  should never cost a round trip — value objects and client-side checks catch
  it at the call site.
- **Own the boring, error-prone parts.** Token caching and refresh, retry with
  backoff honouring `Retry-After`, batch limits, stream reconnect. Every
  integration needs these; none should write them.
- **Speak .NET, not HTTP.** `CancellationToken` everywhere, `ILogger` and
  `ActivitySource` for observability, `IServiceCollection`/`IHttpClientFactory`
  for lifetimes. No `Async` suffix — the `Task` says it.
- **Make errors actionable.** Occtoo's `requestId`/`traceId` belongs on the
  error and in the log, so a support ticket starts with an identifier.
- **Types should teach the model.** What a property accepts, which credential
  fits which situation — discoverable from IntelliSense, not a table.
- **Nothing hidden that a consumer might need to control.** Timeouts, retries,
  the `HttpClient`, caches, the base address: good defaults, all overridable.

## Settled decisions

- **Errors are results.** `Result<T, OcctooError>` from
  `CSharpFunctionalExtensions` everywhere; the typed hierarchy is the decision
  surface (`TransientError` means retry). Exceptions are reserved for
  construction mistakes, cancellation, and the genuinely unexpected. The one
  structural carve-out: `OcctooAuthenticationHandler` throws
  `OcctooCredentialException` because a `DelegatingHandler` has no result
  channel; SDK surfaces convert it back. See [errors.md](errors.md).
- **Credentials are a strategy.** `IOcctooCredential` behind the
  `OcctooCredential` factory; concrete flows are internal. Implemented: API
  key, client credentials, device code, `FromDelegate`. Deliberately absent:
  the legacy data-provider exchange (serves only the legacy import surface this
  SDK does not wrap) and a fixed-token credential (whoever holds a raw token
  has a real flow available, and a pasted token dies within the hour).
- **The authority defaults to `https://auth.occtoo.com`**, overridable.
  `ClientId` and `Audience` are required with no defaults — a guessed audience
  yields an unexplained `401`.
- **Tokens are cached in FusionCache** — in-memory by default, any distributed
  provider by supplying an `IFusionCache`. Cache keys embed a hash of the
  secret, never the secret.
- **Retries are built in** via `Microsoft.Extensions.Http.Resilience`,
  configured on `OcctooClientOptions.Resilience`, honouring `Retry-After`.
- **Trim/native-AOT compatibility is a hard requirement**, enforced by
  analyzers and a CI smoke test — see
  [conventions.md](conventions.md#trimming-and-native-aot).

## Open decisions

- **Automatic batching for ingest.** Today `IngestEntries` takes one batch and
  documents the recommended 1000-entry ceiling. An automatic batcher over the
  documented limits — with a legible answer for "batch 3 of 7 failed" — remains
  open.
- **Events: cursors and typing.** When the Events surface lands: a cursor type
  that carries its filter (so "store the cursor" cannot mean "store half of
  it"), and typed payloads for known event types with the raw `JsonElement`
  and unknown types surfaced rather than dropped.
- **Authorization code with PKCE.** A better desktop experience than device
  code when a browser and loopback redirect exist; deferred because device
  code covers interactive sign-in without binding a port.

## Non-goals

- Wrapping the destination/consumption APIs — per-tenant generated APIs, the
  wrong shape for a hand-written client.
- Anything beyond the documented public contract.
- Hiding HTTP. A consumer who needs the raw response should be able to get it.
