# Image conversions

`client.ImageConversions` wraps `/v1/image-conversions`: upload an image once, then convert it as often as you like. Each conversion resizes the original onto a canvas, re-encodes it and returns a temporary download link.

```csharp
var thumbnail = new ImageConversionParameters(
    ImageConversionMode.Fit, TargetSize: 400, AspectRatio: "1:1", ImageOutputFormat.Webp, Quality: 80);

var large = new ImageConversionParameters(
    ImageConversionMode.FillBackground, TargetSize: 2000, AspectRatio: "4:3", ImageOutputFormat.Jpg, Quality: 90)
{
    PaddingPercent = 5,
    Background = ImageBackground.From("FFFFFF"),
};

await client.ImageConversions.Upload(AssetContent.FromFile("files/shoe.tif"), "shoe.tif")
    .Bind(id => client.ImageConversions.Convert(id, thumbnail)
        .Tap(image => logger.LogInformation("Thumbnail at {Url}", image.Url))
        .Bind(_ => client.ImageConversions.Convert(id, large)))
    .Tap(image => logger.LogInformation("Large image at {Url}", image.Url))
    .TapError(error => logger.LogWarning("Conversion failed: {Error}", error));
```

Both calls need a credential with `write:media`; see [authentication.md](authentication.md).

## Upload

`Upload` takes the image's bytes as an `AssetContent`, the same type asset uploads use (a file, a buffer, a stream or a delegate; see [assets.md](assets.md)), and its filename as an `AssetFilename`. It:

1. works out the content's length, and fails with a `ValidationError` before any call if it can't;
2. asks Occtoo for an upload link, which lasts 1 hour and comes with `maxFileSize`, the largest original Occtoo accepts;
3. fails with a `ValidationError` naming both numbers if the content is `maxFileSize` bytes or more, before any bytes are sent;
4. sends the bytes straight to Azure Blob Storage with a single `PUT` against the signed link, with no Occtoo credential;
5. returns the `ImageConversionId` to convert with.

The transfer isn't retried.

## Convert

`Convert` takes the id and either an `ImageConversionParameters` or the id of one of your [presets](#presets), and returns a `ConvertedImage`: the download `Url`, its `ExpiresAt`, the `Filename`, the `ContentType` and the `Size` in bytes. Every call converts the original again and returns a fresh link.

```csharp
var image = await client.ImageConversions.Convert(id, "web-thumbnail");
```

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

## Presets

A preset is a named set of `ImageConversionParameters` stored in your tenant. `client.ImageConversions` lists, reads, creates, replaces and deletes them, and `Convert` takes a preset's id in place of the parameters (see [Convert](#convert)).

```csharp
await client.ImageConversions.CreatePreset("web-thumbnail", "Web thumbnail", thumbnail)
    .Bind(created => client.ImageConversions.GetPreset(created.Id))
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

A card definition can use a preset. While one does, `DeletePreset` fails with a `ConflictError`.

Reading presets needs `read:media`; creating, updating and deleting them needs `write:media`.

## Lifetimes

- The upload link lasts 1 hour.
- Each download link lasts 1 hour.
- You can convert the original any number of times until Occtoo deletes it, a day after the upload.

## What can go wrong

| Failure | Error |
|---|---|
| The content's length is unknown, or the content is too large | `ValidationError`, before any bytes are sent |
| A parameter breaks a rule | `ValidationError`, with `Failures` keyed `Parameters.TargetSize`, `Parameters.PaddingPercent`, `Parameters.Quality`, `Parameters.Mode` or `Parameters.OutputFormat` |
| The original is too large, isn't an image Occtoo can convert, or the filename is refused | `ValidationError`, without `Failures` |
| No original under the id: unknown, not uploaded yet, or already deleted; or, converting with a preset, no preset under its id. The error doesn't say which | `NotFoundError` |
| The credential is refused, or lacks `write:media` | `AuthenticationError`, `ForbiddenError` |
| The conversion ran over 25 seconds (`504`), or Occtoo failed | `ServerError` |
| Blob storage refused the bytes | As for an asset transfer: an expired upload link is an `AuthenticationError`, and the message carries storage's error code and request id |
| A preset's id, display name or parameter breaks a rule | `ValidationError`, with `Failures` keyed `Id`, `DisplayName` or `Parameters.TargetSize` and the like |
| A page limit outside 1 to 200, or a cursor Occtoo refuses | `ValidationError`; a refused cursor is keyed `after` |
| No preset under the id, on `GetPreset` or `UpdatePreset` | `NotFoundError` |
| `CreatePreset` with an id that already exists, or `DeletePreset` while a card definition uses the preset | `ConflictError` |
| The credential lacks `read:media` to read presets, or `write:media` to change them | `ForbiddenError` |

The SDK doesn't resend the create request or `Convert` after a `5xx` or a timeout. See [errors.md](errors.md).
