using System.Globalization;
using Occtoo.Events;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventSecretMaskingTests
{
    [Fact]
    public void A_secret_never_prints_with_or_without_a_format_provider()
    {
        string[] printed =
        [
            $"{WebhookSigningSecret.From("s3cret")}", WebhookSigningSecret.From("s3cret").ToString(CultureInfo.InvariantCulture),
            $"{AzureConnectionString.From("s3cret")}", AzureConnectionString.From("s3cret").ToString(CultureInfo.InvariantCulture),
        ];

        printed.ShouldAllBe(text => text == "***");
    }
}
