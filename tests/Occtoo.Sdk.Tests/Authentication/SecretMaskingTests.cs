using System.Globalization;
using Occtoo.Authentication;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Authentication;

public class SecretMaskingTests
{
    [Fact]
    public void A_secret_never_prints_with_or_without_a_format_provider()
    {
        string[] printed =
        [
            $"{ClientSecret.From("s3cret")}", ClientSecret.From("s3cret").ToString(CultureInfo.InvariantCulture),
            $"{ApiKey.From("s3cret")}", ApiKey.From("s3cret").ToString(CultureInfo.InvariantCulture),
        ];

        printed.ShouldAllBe(text => text == "***");
    }
}
