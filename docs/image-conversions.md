# Image conversions

`client.ImageConversions` wraps `/v1/image-conversions`: upload an image once, then convert it as often as you like. Each conversion resizes the original onto a canvas, re-encodes it and returns a temporary download link. Nothing is stored in the tenant's media.

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

`Convert` takes the id and an `ImageConversionParameters`, and returns a `ConvertedImage`: the download `Url`, its `ExpiresAt`, the `Filename`, the `ContentType` and the `Size` in bytes. Every call converts the original again and returns a fresh link.

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
| No original under the id: unknown, not uploaded yet, or already deleted | `NotFoundError` |
| The credential is refused, or lacks `write:media` | `AuthenticationError`, `ForbiddenError` |
| The conversion ran over 25 seconds (`504`), or Occtoo failed | `ServerError` |
| Blob storage refused the bytes | As for an asset transfer: an expired upload link is an `AuthenticationError`, and the message carries storage's error code and request id |

The SDK doesn't resend the create request or `Convert` after a `5xx` or a timeout. See [errors.md](errors.md).
