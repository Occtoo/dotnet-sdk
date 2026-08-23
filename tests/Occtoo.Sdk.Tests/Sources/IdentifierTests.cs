using Occtoo.Sources;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Sources;

public class IdentifierTests
{
    [Fact]
    public void Property_ids_are_lowercased_the_way_occtoo_stores_them()
    {
        PropertyId.From("PublishedAt").Value.ShouldBe("publishedat");
        PropertyId.From("PublishedAt").ShouldBe(PropertyId.From("publishedat"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ids_reject_blank_input(string input)
    {
        Should.Throw<ValueObjectValidationException>(() => SourceId.From(input));
        Should.Throw<ValueObjectValidationException>(() => EntryId.From(input));
        Should.Throw<ValueObjectValidationException>(() => PropertyId.From(input));
    }

    [Fact]
    public void Ids_enforce_the_length_limit()
    {
        var tooLong = new string('x', 257);

        Should.Throw<ValueObjectValidationException>(() => SourceId.From(tooLong));
        Should.Throw<ValueObjectValidationException>(() => EntryId.From(tooLong));
        Should.Throw<ValueObjectValidationException>(() => PropertyId.From(tooLong));
        EntryId.From(new string('x', 256)).Value.Length.ShouldBe(256);
    }

    [Fact]
    public void Strings_convert_implicitly_through_the_same_validation()
    {
        // The builder's signatures take the value objects; string literals reach
        // them through this conversion, so validation cannot be skipped.
        PropertyId propertyId = "PublishedAt";
        propertyId.Value.ShouldBe("publishedat");

        LanguageCode language = "sv-se";
        language.Value.ShouldBe("sv-SE");

        Should.Throw<ValueObjectValidationException>(() =>
        {
            LanguageCode invalid = "not-a-language";
            _ = invalid;
        });

        Should.Throw<ValueObjectValidationException>(() =>
            SourceEntry.WithId("sku-1").WithText(new string('x', 300), "value"));
    }

    [Fact]
    public void Try_from_reports_invalid_input_without_throwing()
    {
        SourceId.TryFrom("", out _).ShouldBeFalse();
        SourceId.TryFrom("products", out var sourceId).ShouldBeTrue();
        sourceId.Value.ShouldBe("products");
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("sv-se", "sv-SE")]
    [InlineData("sv-SE", "sv-SE")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("zh-Hans-CN", "zh-Hans-CN")]
    [InlineData("es-419", "es-419")]
    public void Language_codes_accept_iso_codes_and_normalize_their_casing(string input, string expected)
    {
        LanguageCode.From(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("english")]
    [InlineData("xx")]
    [InlineData("en-")]
    [InlineData("en-US-Extra-Long")]
    [InlineData("12")]
    public void Language_codes_reject_non_iso_input(string input)
    {
        Should.Throw<ValueObjectValidationException>(() => LanguageCode.From(input));
    }
}
