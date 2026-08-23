# Sources: typed ingest

`client.Sources` wraps the typed ingest endpoint,
`POST /sources/{sourceId}` — JSON values validated against the source's property
configuration and queued for asynchronous processing.

```csharp
var entry = SourceEntry.WithId("sku-123")
    .WithLocalizedText("name", "Blue chair", "en")
    .WithDecimal("price", 100.111m)
    .WithBoolean("inStock", true)
    .WithTimestamp("publishedAt", DateTimeOffset.UtcNow)
    .WithList("tags", "summer", "sale");

var outcome = await client.Sources
    .IngestEntries(SourceId.From("products"), [entry])
    .Tap(receipt => logger.LogInformation(
        "accepted {Count} entries, correlation {Id}",
        receipt.AcceptedEntryCount, receipt.CorrelationId.Value))
    .TapError(error => logger.LogWarning("rejected: {Error}", error));
```

Requires a tenant-level application credential with the `write:sources` scope —
see [authentication.md](authentication.md).

## The model

Identifiers are value objects that enforce the API's rules at construction:
`SourceId` (non-empty), `EntryId` and `PropertyId` (1–256 characters;
property ids are lowercased, the way Occtoo stores them), and `LanguageCode`
(ISO-based: `en`, `sv-SE`, `zh-Hans-CN`, normalized casing). `From` throws for
invalid input, `TryFrom` reports it — use the latter for untrusted data.

The builder's signatures take these types — `WithText(PropertyId, string)`,
`WithLocalizedText(PropertyId, string, LanguageCode)` — and each identifier also
converts implicitly from a string *through the same validation*, so the example
above compiles from literals while `"english"` as a language or a 300-character
property id still fails at the call site, before any round trip.

Values are a closed union, `PropertyValue`, matching exactly what the endpoint
accepts:

| Factory | JSON on the wire | Source property type |
|---|---|---|
| `PropertyValue.Text("...")` | string | `Text` / `LocalizedText` |
| `PropertyValue.Integer(10)` | number | `Integer` |
| `PropertyValue.Decimal(100.111m)` | number | `Decimal` |
| `PropertyValue.Boolean(true)` | boolean | `Boolean` |
| `PropertyValue.Timestamp(when)` | ISO 8601 string | `Timestamp` |
| `PropertyValue.List("a", "b")` | array of strings | `List` / `LocalizedList` |
| `PropertyValue.Clear` | `null` | clears a configured property |

Implicit conversions cover the common cases, so `new EntryProperty(id, "text")`
and `new EntryProperty(id, 100.5m)` also work. The builder
(`SourceEntry.WithId(...).With*(...)`) is the ergonomic path; the records
underneath (`SourceEntry`, `EntryProperty`) are there when you are mapping from
your own model.

Localized properties (`WithLocalizedText`, `WithLocalizedList`) require a
language; non-localized ones reject it. The same property id may repeat across
distinct languages.

## What comes back

Success is an `IngestReceipt`: the batch `CorrelationId` (keep it — it is what
diagnostics and support work from), the source, the accepted entry count, the
UTC acceptance time, and `NewProperties` — properties the source did not know,
with the types Occtoo inferred from your values.

Acceptance (`202`) means the whole batch passed validation and was queued.
Processing is asynchronous: entries and newly inferred properties may not be
visible immediately. **Do not retry an accepted batch because data is not
visible yet.**

## What can go wrong

Everything returns `Result<IngestReceipt, OcctooError>` — see
[errors.md](errors.md) for the hierarchy. The mapping for this endpoint:

| Response | Error | Retry? |
|---|---|---|
| `400` | `ValidationError` with per-path `Failures` | No — fix the payload |
| `401` | `AuthenticationError` | No — fix the credential |
| `403` | `ForbiddenError` (scope or source grant missing) | No — grant access |
| `404` | `NotFoundError` (source cannot be resolved) | No — fix the id |
| `409` | `ConflictError` (source is being purged) | Later |
| `429` | `RateLimitError` with `RetryAfter` | Yes — honour the delay |
| `5xx` | `ServerError` | Yes — with backoff |
| unreachable / timed out | `NetworkError` / `TimeoutError` | Yes — with backoff |

Validation is all-or-nothing: a `400` means nothing was accepted, and
`ValidationError.Failures` names the offending request paths.

## Batch size

Occtoo recommends at most 1000 entries per request
(`SourcesClient.RecommendedMaxEntriesPerRequest`). The SDK does not reject
larger batches — the limit is a recommendation, not a contract — but splitting
keeps ingestion performing well. Every entry is an upsert; typed ingest has no
delete flag.
