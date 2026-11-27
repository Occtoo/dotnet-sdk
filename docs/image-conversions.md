# Image conversions

`client.ImageConversions` wraps `/v1/image-conversions` and `/v1/conversion-presets`: upload an image once, then convert it as often as you like, with parameters you pass or with one of the tenant's conversion presets. Each conversion resizes the original onto a canvas and re-encodes it. `Convert` queues the conversion and returns a task id; read the task with `GetTask` every second or two until it's done, then download the result from its temporary link.

```csharp
var thumbnail = new ImageConversionParameters(
    ImageConversionMode.Fit, TargetSize: 400, AspectRatio: "1:1", ImageOutputFormat.Webp, Quality: 80);

var queued = await client.ImageConversions.Upload(AssetContent.FromFile("files/shoe.tif"), "shoe.tif", cancellationToken)
    .Bind(id => client.ImageConversions.Convert(id, thumbnail, cancellationToken)
        .Map(taskId => (Id: id, TaskId: taskId)));

if (queued.IsFailure)
{
    logger.LogWarning("Conversion wasn't queued: {Error}", queued.Error);
    return;
}

while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

    var read = await client.ImageConversions.GetTask(queued.Value.Id, queued.Value.TaskId, cancellationToken);
    if (read.IsFailure)
    {
        if (read.Error is TransientError)
        {
            logger.LogWarning("Couldn't read the conversion, retrying: {Error}", read.Error);
            continue;
        }

        logger.LogWarning("Couldn't read the conversion, giving up: {Error}", read.Error);
        break;
    }

    var task = read.Value;
    if (task.Status == ConversionTaskStatus.Completed)
    {
        logger.LogInformation("Thumbnail at {Url}", task.Image.Value.Url);
        break;
    }

    if (task.Status == ConversionTaskStatus.Failed)
    {
        logger.LogWarning("Conversion failed: {Error}", task.Error.GetValueOrDefault());
        break;
    }
}
```

The loop stops when the conversion completes or fails, when a read fails with anything other than a `TransientError` (polling again won't fix those), or when `cancellationToken` is cancelled.

`Upload`, `Convert` and `GetTask` need a credential with `write:media`; see [authentication.md](authentication.md).

## Upload

`Upload` takes the image's bytes as an `AssetContent`, the same type asset uploads use (a file, a buffer, a stream or a delegate; see [assets.md](assets.md)), and its filename as an `AssetFilename`. It:

1. works out the content's length, and fails with a `ValidationError` before any call if it can't;
2. asks Occtoo for an upload link, which lasts 1 hour and comes with `maxFileSize`, the largest original Occtoo accepts;
3. fails with a `ValidationError` naming both numbers if the content is `maxFileSize` bytes or more, before any bytes are sent;
4. sends the bytes straight to Azure Blob Storage with a single `PUT` against the signed link, with no Occtoo credential;
5. returns the `ImageConversionId` to convert with.

The transfer isn't retried.

## Convert

`Convert` takes the id and either an `ImageConversionParameters` or the id of one of your [presets](#presets), queues the conversion and returns its `ConversionTaskId`. Read the result with [`GetTask`](#read-the-result). Every call queues a new conversion with its own output, even with the same parameters.

```csharp
var large = new ImageConversionParameters(
    ImageConversionMode.FillBackground, TargetSize: 2000, AspectRatio: "4:3", ImageOutputFormat.Jpg, Quality: 90)
{
    PaddingPercent = 5,
    Background = ImageBackground.From("FFFFFF"),
};

var withParameters = await client.ImageConversions.Convert(id, large);
var withPreset = await client.ImageConversions.Convert(id, "web-thumbnail");
```

`Convert` fails right away when it can't queue the conversion or when Occtoo fails while queuing it: the credential is refused or lacks the scope, a parameter or the preset is refused, the original is 32 MiB or more, there's no original under the id or, with a preset, no preset under its id, or Occtoo returns a server error or times out (a call that fails this way isn't resent and may still have queued a conversion). See [What can go wrong](#what-can-go-wrong). An original Occtoo can't convert fails the task instead.

| Parameter | Rule |
|---|---|
| `Mode` | `Fit`, `FillBackground`, `Crop` or `Auto` (below) |
| `TargetSize` | The canvas's longer side in pixels, 1 to 8000 |
| `AspectRatio` | The canvas's shape as `width:height`, each side a whole number from 1 to 100 without leading zeros, such as `4:3` |
| `OutputFormat` | `Jpg`, `Png` or `Webp` |
| `Quality` | The encoding quality, 1 to 100 |
| `PaddingPercent` | Optional. The padding on each side as a percentage of `TargetSize`, 0 to 49. Required with `FillBackground`, not allowed with any other mode |
| `Background` | Optional. The color behind transparent areas and padding, six hex digits in either case without `#`, such as `FFFFFF` |

The modes. An original smaller than the canvas is scaled up in every mode.

- `Fit` scales the image to fit inside the canvas, keeping its proportions. One side can come out shorter than the canvas.
- `FillBackground` fits the image inside the canvas less the padding, then fills the rest with the background. The result is exactly the canvas size.
- `Crop` scales the image to cover the canvas and crops what overflows, keeping the center.
- `Auto` crops when the image and the canvas have the same orientation, and fits otherwise.

`ImageAspectRatio` and `ImageBackground` are value objects: `From` throws a `ValueObjectValidationException` for a malformed value and `TryFrom` reports it. `AspectRatio` takes a string literal directly. `Background` is a `Maybe<ImageBackground>`, so assign `ImageBackground.From("FFFFFF")`. Occtoo checks the numeric ranges and the padding rule.

## Read the result

`GetTask` takes the upload's id and the `ConversionTaskId` that `Convert` returned, and returns a `ConversionTask`:

| Field | What it holds |
|---|---|
| `Id` | The task's id |
| `Status` | Where the conversion stands, a `Maybe<ConversionTaskStatus>` (below) |
| `Image` | Once the task is `Completed`: a `ConvertedImage` with the download `Url`, its `ExpiresAt`, the `Filename`, the `ContentType` and the `Size` in bytes |
| `Error` | Once the task is `Failed`: Occtoo's message saying why, such as an original it can't convert |

The statuses:

- `Queued`: the conversion is waiting to run. A running task can go back to `Queued` when Occtoo retries it.
- `Running`: the conversion is running.
- `Completed`: the conversion finished and `Image` is ready to download. This is final.
- `Failed`: the conversion failed and won't be retried. This is final.

Read the task every second or two until it's `Completed` or `Failed`; that usually takes a few seconds. A failed conversion is still a successful read: `GetTask` succeeds with `Status` `Failed` and the message in `Error`. A failed `Result` means the read itself failed.

Every read of a completed task returns a new download link that lasts an hour, so read the task again when a link has expired. A task stays readable for at least a day after it finishes. Once Occtoo no longer keeps the result, or for an id that isn't a task of this upload, `GetTask` fails with a `NotFoundError`.

`Status` is read like every enum in the SDK: a status this SDK version doesn't know reads as absent, as on [asset states](assets.md#reading-state-afterwards). `Image` and `Error` are still filled in when Occtoo sends them.

## Presets

A preset is a named set of `ImageConversionParameters` stored in your tenant. `client.ImageConversions` lists, reads, creates, replaces and deletes them, and `Convert` takes a preset's id in place of the parameters (see [Convert](#convert)).

```csharp
await client.ImageConversions.CreatePreset("web-thumbnail", "Web thumbnail", thumbnail)
    .Ensure(
        preset => preset.Parameters.HasValue,
        new UnexpectedError("The preset uses a mode or an output format this SDK version doesn't know."))
    .Bind(preset => client.ImageConversions.UpdatePreset(
        preset.Id, preset.DisplayName, preset.Parameters.Value with { Quality = 90 }))
    .Tap(preset => logger.LogInformation("Preset {Id} is at version {Version}", preset.Id, preset.Version))
    .TapError(error => logger.LogWarning("Preset change failed: {Error}", error));

var page = await client.ImageConversions.ListPresets(new PageRequest { Limit = 100 });
```

| Call | What it does |
|---|---|
| `ListPresets(page)` | Reads one page of presets, ordered by id. Follow `Next` while `HasMore` is true |
| `GetPreset(id)` | Reads one preset |
| `CreatePreset(id, displayName, parameters)` | Creates a preset and returns it |
| `UpdatePreset(id, displayName, parameters)` | Replaces the display name and the parameters together, and returns the preset |
| `DeletePreset(id)` | Deletes a preset. Deleting one that doesn't exist succeeds |

A `ConversionPreset` carries its `Id`, `DisplayName`, `Parameters`, `Version`, `CreatedAt` and `UpdatedAt`. `Version` goes up when the parameters change. `Parameters` is a `Maybe<ImageConversionParameters>`: it's absent when Occtoo returns a mode or an output format this SDK version doesn't know.

The rules for the inputs, checked by the value objects before any call:

- `ConversionPresetId`: a lower-case letter, then lower-case letters, digits, `_` or `-`, at most 64 characters, such as `web-thumbnail`. It can't change once the preset exists.
- `ConversionPresetDisplayName`: 2 to 50 printable ASCII characters, not starting or ending with a space. Occtoo never trims a name.

Both take a string literal directly. The parameters follow the same rules as for `Convert`.

A card definition can use a preset. While one does, `DeletePreset` fails with a `ConflictError`. If a change made at the same time interferes with the delete, it fails with a `NotFoundError`. `GetPreset` tells you whether the preset is still there.

Reading presets needs `read:media`; creating, updating and deleting them needs `write:media`.

## Lifetimes

- The upload link lasts 1 hour.
- Each download link lasts 1 hour. Every `GetTask` of a completed task returns a new one.
- A task stays readable for at least a day after it finishes.
- You can convert the original any number of times until Occtoo deletes it, a day after the upload.

## What can go wrong

| Failure | Error |
|---|---|
| The content's length is unknown, or the content is too large | `ValidationError`, before any bytes are sent |
| A parameter breaks a rule | `ValidationError`, with `Failures` keyed `Parameters.TargetSize`, `Parameters.PaddingPercent`, `Parameters.Quality`, `Parameters.Mode` or `Parameters.OutputFormat` |
| The original is 32 MiB or more, on `Convert`; or the filename is refused, on `Upload` | `ValidationError`, without `Failures` |
| No original under the id: unknown, not uploaded yet, or already deleted; or, converting with a preset, no preset under its id. The error doesn't say which | `NotFoundError` |
| On `GetTask`, the upload has no task under the id, or the task's result is no longer kept | `NotFoundError` |
| The conversion failed, for example because the original isn't an image Occtoo can convert | No error: `GetTask` succeeds with `Status` `Failed` and Occtoo's message in `Error` |
| The credential is refused, or lacks `write:media`, which `Upload`, `Convert` and `GetTask` need | `AuthenticationError`, `ForbiddenError` |
| Occtoo failed | `ServerError` |
| Blob storage refused the bytes | As for an asset transfer: an expired upload link is an `AuthenticationError`, and the message carries storage's error code and request id |
| A preset's id, display name or parameter breaks a rule | `ValidationError`, with `Failures` keyed `Id`, `DisplayName` or `Parameters.TargetSize` and the like |
| A page limit outside 1 to 200, or a cursor Occtoo refuses | `ValidationError`; a refused cursor is keyed `after` |
| No preset under the id, on `GetPreset` or `UpdatePreset`; on `DeletePreset`, a change made at the same time interfered with the delete (check with `GetPreset`) | `NotFoundError` |
| `CreatePreset` with an id that already exists, or `DeletePreset` while a card definition uses the preset | `ConflictError` |
| The credential lacks `read:media` to read presets, or `write:media` to change them | `ForbiddenError` |

The SDK doesn't resend the create request or `Convert` after a `5xx` or a timeout. Such a `Convert` may still have queued a conversion, and calling it again queues another. See [errors.md](errors.md).
