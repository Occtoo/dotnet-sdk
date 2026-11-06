using Microsoft.Extensions.DependencyInjection;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.ImageConversions;

namespace Occtoo.Sdk.Tests.ImageConversions;

internal static class ImageConversionsTestSetup
{
    internal const string Id = "3f2b6c1e-8a4d-4c7b-9e2f-1a5b6c7d8e9f";

    internal const string OriginalUrl = "https://tenant.blob.core.windows.net/conversions/shoe.tif?sig=abc";

    internal const string DownloadUrl = "https://tenant.blob.core.windows.net/conversions/shoe.webp?sig=def";

    internal const string DownloadExpiry = "2099-01-01T00:00:00+00:00";

    internal static readonly ImageConversionId ConversionId = ImageConversionId.From(Guid.Parse(Id));

    internal static readonly ImageConversionParameters Thumbnail =
        new(ImageConversionMode.Fit, 400, "1:1", ImageOutputFormat.Webp, 80);

    internal static readonly ImageConversionParameters Padded =
        new(ImageConversionMode.FillBackground, 2000, "4:3", ImageOutputFormat.Jpg, 90)
        {
            PaddingPercent = 5,
            Background = ImageBackground.From("FFFFFF"),
        };

    internal const string ConvertedBody = $$"""
        {
          "url": "{{DownloadUrl}}",
          "expiresAt": "{{DownloadExpiry}}",
          "filename": "shoe.webp",
          "contentType": "image/webp",
          "size": 81234
        }
        """;

    internal const string NotFoundBody = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
          "title": "Not Found",
          "status": 404
        }
        """;

    internal const string BadFilenameBody = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "Bad Request",
          "status": 400,
          "detail": "The filename is not valid."
        }
        """;

    internal const string TargetSizeRejectedBody = """
        {
          "title": "One or more validation errors occurred.",
          "status": 400,
          "errors": {
            "Parameters.TargetSize": [ "'Target Size' must be between 1 and 8000." ]
          }
        }
        """;

    internal const string TimedOutBody = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.6.5",
          "title": "Gateway Timeout",
          "status": 504,
          "detail": "The conversion took longer than 25 seconds."
        }
        """;

    /// <summary>The create response, accepting originals smaller than <paramref name="maxFileSize"/> bytes.</summary>
    internal static string Created(long maxFileSize = 33554432) => $$"""
        {
          "id": "{{Id}}",
          "uploadUrl": "{{OriginalUrl}}",
          "uploadExpiresAt": "2099-01-01T00:00:00+00:00",
          "maxFileSize": {{maxFileSize}}
        }
        """;

    /// <summary>
    /// A client whose pipeline retries a failed request, built the way
    /// dependency injection builds it.
    /// </summary>
    internal static OcctooClient RetryingClient(StubHandler api)
    {
        var options = new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            Resilience = new OcctooResilienceOptions
            {
                MaxRetryAttempts = 3,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false,
            },
        };

        var services = new ServiceCollection();
        services.AddOcctooClient(_ => options);
        services
            .AddHttpClient(OcctooServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => api);

        return services.BuildServiceProvider().GetRequiredService<OcctooClient>();
    }
}
