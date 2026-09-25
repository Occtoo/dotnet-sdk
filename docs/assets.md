# Assets: uploading files

`client.Assets` wraps `/v1/assets/{dataSourceId}` — the endpoints that put a file into a **Media** data source. An asset is one file: Occtoo creates the entry and signs an upload link, the bytes go straight from your process to Azure Blob Storage, and Occtoo then reads the blob back and makes a file of it.

```csharp
var report = await client.Assets.Upload(
    SourceId.From("product-media"),
    [
        AssetUpload.FromFile("logo_1", "files/logo.png"),
        AssetUpload.FromFile("note_1", "files/note.txt"),
    ],
    new AssetUploadOptions { Progress = dashboard });

report.Match(
    uploaded =>
    {
        foreach (var completed in uploaded.Completed)
            logger.LogInformation("{Key} is at {Url}", completed.Key.Value, completed.Outcome.Value.PublicUrl);

        foreach (var failed in uploaded.Failed)
            logger.LogWarning("{Key} stopped at {Stage}: {Error}", failed.Key.Value, failed.Reached, failed.Outcome.Error);
    },
    error => logger.LogError("The upload could not run: {Error}", error));
```

The run and the assets in it fail separately, which is why the example above never reads a `Result` without checking it first: `Completed` and `Failed` have already done that checking for you, so every outcome in one has a file and every outcome in the other has an error.

Requires a tenant-level application credential with `write:media`, `write:sources` or `import-datasource` — see [authentication.md](authentication.md). The data source must be a Media one; anything else is rejected with a `ValidationError` saying so.

## The model

| Type | What it is |
|---|---|
| `SourceId` | The data source, the same identifier typed ingest uses |
| `AssetKey` | The asset's key within that data source — also the id of the entry the upload creates |
| `AssetFilename` | The name the file is stored under |
| `FolderId` | Optional; the folder the entries land in, root when omitted |
| `Asset` | A key and a filename together, which is what every call takes |
| `AssetUpload` | An `Asset` plus its bytes, which is what `Upload` takes |

`AssetKey` allows **letters, digits, `_` and `-` only**, up to 256 characters. That is stricter than `EntryId`, and it is the rule people trip over first: `note.txt` is not a valid key, `note_1` is. A filename is up to 200 characters, must name a file rather than a path, must not start or end with a space or a dot, and must not contain `..`.

Both are value objects: `From` throws for invalid input, `TryFrom` reports it, and each converts implicitly from a string through the same validation — so the literals in the example above are checked at the call site, before any round trip. `AssetKey.AsEntryId()` hands the key to `client.Sources` when you want to write other properties onto the same entry.

The same filename must be used to initialize and to complete an asset, because the name is part of the blob's path. Passing `Asset` — the pair — to both calls is what makes that impossible to get wrong. Occtoo's initialize response carries only the upload URL and its expiry, so the SDK threads the filename from your own request onto each `AssetUploadLink`: `link.Asset` is the pair to complete with.

## Upload

`Upload` is the call to reach for. It initializes, transfers and completes, and owns the parts that are easy to get wrong:

- assets transfer several at a time (`MaxConcurrentTransfers`, four by default and 100 at most — one per asset in the largest run);
- a transfer that fails for a reason worth retrying is sent again (`MaxTransferAttempts`, three by default and ten at most): the content is reopened first, and each attempt waits longer than the last — or as long as storage's `Retry-After` says when it answers with one, capped at two minutes, since the wait holds a transfer slot against a link that expires in an hour;
- an asset gets one re-sign per run, covering both ways a link can go bad: a link the SDK already sees as expired is re-signed before any bytes go out, and a link storage rejects mid-transfer is re-signed with the bytes sent again — the second re-sign also needs content that can be opened again. The first check is just your clock against a server-issued expiry, so a failed re-sign doesn't stop the upload: the bytes go to the old link anyway, and if it really was dead, storage's own `403` still triggers the re-sign;
- Occtoo initializes up to 100 assets per call but completes only 50, so a large run is completed in more than one call;
- nonsense options are refused locally before any request goes out, as a `ValidationError` on the run's own `Result` — `Upload` doesn't throw for them.

What comes back is an `AssetUploadReport`: one `AssetUploadOutcome` per asset, in the order you passed them. Each outcome carries `Reached` — how far that asset got — and `Outcome`, the same `Result<AssetFileInfo, OcctooError>` shape as everything else in the SDK, so a `TransientError` still means "this one is worth another run". `Completed` and `Failed` split the outcomes, and `AllCompleted` is true when every asset has a file.

A **failed `Result` from `Upload` itself** means the run could not proceed at all: a local validation failure, a rejected credential, a `429`, an unreachable API. Nothing was uploaded. A **successful result with failures inside it** means the run happened and some assets did not make it.

```csharp
var outcome = await client.Assets.Upload(dataSourceId, uploads, options, ct);

outcome.Match(
    report =>
    {
        foreach (var failed in report.Failed)
            logger.LogWarning("{Key} stopped at {Stage}: {Error}", failed.Key.Value, failed.Reached, failed.Outcome.Error);
    },
    error => logger.LogError("The run never started: {Error}", error));
```

Re-running a failed asset is safe: initializing a key that already exists signs a fresh link for it. Replacing the file of an asset that **completed** means deleting it first — `RefreshUploadLinks` refuses a completed asset, and completing an asset twice is refused as well.

## The steps underneath

The primitives are public, for callers who have to drive the sequence themselves — a link handed to another process, a transfer that happens on another machine, an upload resumed after a restart.

| Call | Does | Ceiling |
|---|---|---|
| `Initialize` | Creates the assets and signs an upload link per key | 100 |
| `Transfer` | Sends one asset's bytes to its link | — |
| `Complete` | Tells Occtoo to read the blob back and create the file | 50 |
| `RefreshUploadLinks` | Signs a fresh link for assets that are already initialized | 50 |
| `GetState` | Reads what Occtoo knows about the given keys | 50 |
| `Delete` | Deletes the assets and the files behind them | 50 |

The ceilings are contract limits, not recommendations, so the SDK rejects an oversized batch locally rather than spending a round trip to be told the same thing. They are public constants — `AssetsClient.MaxAssetsPerInitialize`, `MaxAssetsPerCompletion` and `MaxKeysPerRequest` — so chunk against those rather than against literals. The same goes for the rule that a call must name at least one asset, and that its keys must be distinct.

`Initialize`, `Complete` and `RefreshUploadLinks` return an `AssetBatch<T>`: `Succeeded` keyed by asset, `Failed` carrying Occtoo's own reason per key, `Find(key)` for one key as a `Maybe<T>`, and `AllSucceeded` when nothing was refused. The call succeeding and every key in it succeeding are different things.

`Delete` answers `204` and returns `UnitResult<OcctooError>` — there is nothing to hand back but the failure track. It composes with `Bind`, `Tap` and `TapError` like every other result.

`AssetUploadLink.HasExpired()` reads your clock against the link's expiry, and takes an optional `TimeProvider` so a test can drive it.

## The transfer

The bytes never pass through Occtoo. The upload link is a signed URL for the tenant's own storage account, and the SDK sends the bytes to it on a **separate, unauthenticated `HttpClient`** — an Occtoo token has no business leaving the process on a request to a third party.

How you reach that client depends on how you registered the SDK. `AddOcctooClient` puts it under the named client `Occtoo.Uploads` (`OcctooServiceCollectionExtensions.UploadHttpClientName`), which you can configure with a proxy or your own primary handler. `AddKeyedOcctooClient` appends a suffix unique to that registration, so there is no name to configure — set `OcctooClientOptions.UploadHttpClient` instead, which is also how you supply the client without dependency injection at all.

Two mistakes are refused at construction rather than at the first upload: passing the same `HttpClient` as both the Occtoo client and `UploadHttpClient`, and supplying an upload client that already carries an `Authorization` or `x-api-key` header. Both throw `InvalidOperationException`.

Two things have to hold for that client. **Nothing may retry the request** — the body is a stream, and a replay sends one that has already been read. Retries live in `Upload`, which reopens the content first. **Nothing may put a total-request timeout on it** — a transfer takes as long as the file takes, and the client the SDK registers has none.

A standard resilience handler breaks both, and `services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler())` puts one on every named client, the upload client included. If that is how your host configures resilience, do one of: configure it per client instead of as a default, reach the upload client by `UploadHttpClientName` and undo it there, or supply the transport yourself through `OcctooClientOptions.UploadHttpClient` — the SDK uses the one you supply in place of the one it registers.

Where the bytes come from is an `AssetContent`:

| Factory | Length from | Retryable |
|---|---|---|
| `AssetContent.FromFile(path)` | the file | yes |
| `AssetContent.FromBytes(buffer)` | the buffer | yes |
| `AssetContent.FromStream(stream)` | `Length - Position`; requires a seekable stream | yes |
| `AssetContent.FromStream(stream, length)` | the caller | only if the stream is seekable |
| `AssetContent.From(open, length)` | the caller | yes — the delegate opens it again |

The length has to be known before the transfer starts: with no `Content-Length` the request would be chunked, and Azure Blob Storage rejects that for an upload. A non-seekable stream with no stated length is refused before any bytes move — `Upload` fails with a `ValidationError` naming the overload to use, rather than throwing at the `AssetContent.FromStream` call itself. The SDK never buffers content to memory or to a temp file to discover its length.

One upload carries at most 5000 MiB (`AssetsClient.MaxContentLength`), which is what a single request to blob storage accepts. The SDK checks it locally — finding out after transferring several gigabytes is the most expensive way to learn it.

A transfer is one request and **cannot be resumed**. A link lives one hour; if it expires mid-transfer, the bytes start again against a fresh link.

The `Content-Type` the SDK sends is `application/octet-stream` and is cosmetic: Occtoo determines the real mime type by reading the blob during `Complete`, and `AssetFileInfo.MimeType` is that reading. The image dimensions on `AssetFileInfo` come from the same place, and are `Maybe<int>` because a file that is not an image has none.

`OcctooClientOptions.Timeout` does not bound a transfer — it is the per-request API timeout, and 100 seconds is not a sane limit for moving a file. `AssetUploadOptions.TransferTimeout` (and `AssetTransferOptions.Timeout` for the primitive) bounds one transfer and defaults to no limit; the link's one-hour expiry and your own `CancellationToken` are the real bounds.

There is one timeout the SDK does not own. An upload client it creates itself runs with none, but a client you supply keeps its own `HttpClient.Timeout`, and .NET's default of 100 seconds will cut a large upload short partway through. Set `Timeout.InfiniteTimeSpan` on a client you supply.

## Watching it happen

`AssetUploadOptions.Progress` takes an `IProgress<AssetProgress>`. Each asset reports once when it starts initializing, once when its transfer starts, at intervals while the bytes move, once when it starts completing, and once when it is done; a failure ends that asset with a single report carrying the stage it reached, the error, and how many bytes had moved by then. Byte-level reports are throttled to at most one per 100 ms, and the last report of a transfer carries the full count. A retried transfer rereads its content from the start and reports bytes from zero again, so `BytesTransferred` **goes backwards** mid-asset — a progress bar has to follow it down rather than assume it only rises.

Cancelling the run throws `OperationCanceledException`, as it does everywhere else in the SDK, but every asset gets its ending report first: a `CancelledError` on the stage it had reached, carrying the bytes that had moved.

**The handler must be thread-safe.** Assets transfer in parallel, so up to `MaxConcurrentTransfers` threads report at once and your handler is re-entered. Guard whatever it writes into — a bare `List<T>.Add` loses reports or throws, and an exception thrown in the handler leaves the run.

**Implement `IProgress<T>` yourself rather than using `Progress<T>`.** The SDK reports on the thread that produced the report, so one asset's reports arrive in the order they happened; there is no order between assets. `Progress<T>` gives up even that much: it captures the synchronization context at construction; a console application has none, so its reports are queued to the thread pool, and two reports about the same asset can arrive out of order — a stale `Transferring` after `Completed`.

Render from the progress reports, not from a poll. See the lag note below.

## What can go wrong

| Response | Error | Retry? |
|---|---|---|
| `400` | `ValidationError` — a ceiling, a key, a filename, or a data source that is not Media | No — fix the request |
| `401` | `AuthenticationError` | No — fix the credential |
| `403` | `ForbiddenError` (scope or data source grant missing) | No — grant access |
| `404` | `NotFoundError` — the route did not match | No |
| `409` | `ConflictError` — **the data source does not exist**, is being purged, or the folder does not exist | Later |
| `429` | `RateLimitError` with `RetryAfter` | Yes — honour the delay |
| `5xx` | `ServerError` | Yes — with backoff |
| unreachable / timed out | `NetworkError` / `TimeoutError` | Yes |

**A data source that does not exist answers `409`, not `404`.** Branching on `NotFoundError` for a bad data source id will never fire. The same goes for a folder id that does not resolve.

These endpoints answer in RFC 9457 problem details, same as everything else in the SDK. `ValidationError.Failures` carries Occtoo's per-field messages when the response has them, and every message ends with the `traceId` — that's what a support ticket starts from.

A failed transfer is not an Occtoo response at all, so its errors name the asset and carry storage's own `x-ms-error-code` and `x-ms-request-id`: `Uploading 'logo.png' to blob storage failed: …`. An expired or invalid link is an `AuthenticationError` — `RefreshUploadLinks` is what fixes it, and `Upload` does that by itself.

A key that Occtoo refused inside a batch is an `AssetRejectedError` carrying Occtoo's reason ("Asset is not initialized", "Asset already completed — delete it and initialize again to replace the file"). Repeating the same call reproduces it; what fixes it depends on the reason. See [errors.md](errors.md).

## Reading state afterwards

`GetState` returns what Occtoo knows per key, and **a key it never initialized is simply absent from the dictionary** rather than reported as a status of its own.

`Status` is a `Maybe<AssetStatus>`, read the way every enum in the SDK is read: a status this SDK version doesn't know — one the platform added after it shipped — reads as absent rather than failing the whole response. Treat absent as "can't tell", and update the SDK if you keep seeing it.

Its status trails a successful `Complete` by one event hop: completion creates the file, and the entry's status is updated when the resulting event reaches the service that owns it. An asset read immediately after completing can still say `Initialized`, which is not a failure and not something to poll away. `Complete` already tells you the filename, mime type, size and public URL — use `GetState` to recover state you lost, not to confirm an upload you just finished.

## What it costs

Every one of these endpoints charges the tenant's `assets.requests` budget, and each call costs **one request however many assets it carries** — initializing, refreshing links, completing, reading state and deleting alike. So the ceilings above are also the shape of a cheap integration: one `Upload` of 100 assets spends three requests — one initialize and two completes — against the two hundred that uploading them one at a time would spend. The transfers themselves go to storage and cost nothing here.

A re-sign is a request of its own, and `Upload` re-signs one asset at a time — a run where ten links went bad spends ten extra requests, not one.

Exhausting the budget answers `429` with a `Retry-After`, which arrives as a `RateLimitError` carrying it.
