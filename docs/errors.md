# Errors and results

Failures the SDK anticipates arrive as values: every public operation returns
`Result<T, OcctooError>`
([CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions)),
and the same discipline holds internally. Exceptions are reserved for
programming mistakes caught at startup — dependency-injection wiring, options
validation, value-object construction from invalid input (`SourceId.From("")`;
use `TryFrom` when the input is untrusted) — plus cancellation and conditions
nobody anticipated. That last category can never be ruled out, so the honest
contract is: anything worth *handling* is in the result; what still throws is
worth *crashing on* (or fixing).

## The hierarchy

`OcctooError` is a closed set of record types. The shape of the hierarchy is the
decision surface — consumers branch on *kind*, not on message text:

```
OcctooError                        what happened                        what to do
├─ TransientError                  (abstract) expected to pass          retry with backoff
│  ├─ NetworkError                 endpoint unreachable                 retry
│  ├─ TimeoutError                 no answer in time                    retry
│  ├─ RateLimitError               429, budget exhausted                wait RetryAfter, then retry
│  └─ ServerError                  5xx on Occtoo's side                 retry
├─ AuthenticationError             credential rejected / not established fix the credential
├─ ForbiddenError                  missing scope or resource grant      grant access
├─ NotFoundError                   resource does not exist              fix the id
├─ ConflictError                   resource state rejects the request   wait or resolve
├─ ValidationError                 payload rejected before processing   fix the payload (see Failures)
├─ AssetRejectedError              one asset of a batch was refused     read the reason; repeating won't help
├─ CancelledError                  you cancelled the run                nothing — progress only, see below
├─ DataTypeError                   response value does not fit its type update the SDK / report it
└─ UnexpectedError                 unclassifiable response              investigate
```

Results compose as pipelines — hang side effects on the rails with `Tap` /
`TapError`, chain the next call with `Bind`, and collapse at the very end with
`Finally`. A retry policy needs exactly one type test:

```csharp
return await client.Authenticate(ct)
    .Bind(_ => client.Sources.IngestEntries(sourceId, entries, ct))
    .Tap(receipt => logger.LogInformation("accepted, correlation {Id}", receipt.CorrelationId.Value))
    .TapError(error => logger.LogWarning("ingest failed: {Error}", error))
    .Finally(result => result switch
    {
        { IsSuccess: true } => Done(result.Value),
        { Error: RateLimitError { RetryAfter.HasValue: true } e } => RetryAfter(e.RetryAfter.Value),
        { Error: TransientError } => RetryWithBackoff(),
        _ => GiveUp(result.Error),
    });
```

## Built-in retries

Before a `TransientError` ever reaches you, the SDK has already retried it —
`Microsoft.Extensions.Http.Resilience` (Polly) sits inside the client's
pipeline, on by default: three attempts of jittered exponential backoff, and a
`429`'s `Retry-After` header honored exactly (capped at
`OcctooResilienceOptions.MaxDelay`, default two minutes). Every knob lives on
the options:

```csharp
new OcctooClientOptions
{
    Credential = ...,
    Resilience = new OcctooResilienceOptions
    {
        MaxRetryAttempts = 5,
        BaseDelay = TimeSpan.FromSeconds(2),
        // Enabled = false to own retries yourself
    },
}
```

So the retry policy sketched above is the *outer* layer — what to do when a
failure survives the SDK's own attempts. Retries wait inside the request, which
means `OcctooClientOptions.Timeout` bounds the whole sequence, and each retry
logs a Warning under `Occtoo.Http`. Non-transient errors are never retried:
replaying a `ValidationError` reproduces it.

Only requests that are safe to send twice are replayed after an ambiguous
failure — reads, and ingest, whose entries are upserts. A `5xx`, timeout or
dropped connection on any other write (creating an application, updating one
under an etag, deleting) may hide a change that was committed, so it surfaces
as its `TransientError` instead: replaying would turn a successful create into
a `ConflictError` and lose its one-time client secret. Check the resource
before trying again. A `429` is resent for every request — it was refused
before anything was processed.

Notes on individual types:

- **`RateLimitError.RetryAfter`** is a `Maybe<TimeSpan>` from the `Retry-After`
  header. Honour it when present; otherwise back off exponentially.
- **`ValidationError.Failures`** carries Occtoo's validation messages keyed by
  request path (`entries[0].properties[1].value`), straight from the RFC 9457
  problem details body. Occtoo validates all-or-nothing, so nothing was accepted.
- **`AuthenticationError.ErrorCode`** is the OAuth `error` code
  (`invalid_client`, `access_denied`, ...) when an authorization server supplied
  one.
- **`AssetRejectedError`** is one asset of a batch refused while the call as a
  whole succeeded — it carries Occtoo's own reason for that key. It appears in
  an `AssetUploadReport`; see [assets.md](assets.md).
- **Blob storage failures** are not Occtoo responses, so they name the asset and
  carry storage's `x-ms-error-code` and `x-ms-request-id` instead of a
  `traceId`. They still land on the same types: an expired upload link is an
  `AuthenticationError`, a busy account a `RateLimitError`.
- Messages embed Occtoo's `requestId`/`traceId` when the response carried one —
  that is what a support ticket needs.

## No nulls

Optional values are `Maybe<T>`, never `null`: `RateLimitError.RetryAfter`,
`DeviceCodeInfo.VerificationUriComplete`, `InferredProperty.Delimiter`,
`Authenticate`'s `Maybe<AccessToken>`. What you can hold, you can use.

## The one deliberate exception

`OcctooAuthenticationHandler` is a `DelegatingHandler`, and a handler has no
result channel — so when a credential cannot be established it throws
`OcctooCredentialException`, which carries the `OcctooError`. The SDK's own
surfaces catch it and return the error as a failed result; it reaches user code
only when the handler is used in a hand-built `HttpClient` pipeline.

## Cancellation

Cancellation stays idiomatic .NET: cancelling the `CancellationToken` you passed
surfaces as `OperationCanceledException`, not as an error result. A *timeout*
the SDK hit on your behalf, by contrast, is a `TimeoutError` — you asked for the
operation, and it failed; you did not ask for it to stop.

`CancelledError` never reaches a failure track. It rides the last
`AssetProgress` of every asset a cancelled `Upload` had not settled; the run
itself still leaves as `OperationCanceledException`.
