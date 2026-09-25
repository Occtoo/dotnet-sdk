using Occtoo.Assets;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetIdentifierTests
{
    [Theory]
    [InlineData("logo")]
    [InlineData("LOGO-2")]
    [InlineData("note_1")]
    [InlineData("0123456789")]
    public void An_asset_key_accepts_letters_digits_underscores_and_hyphens(string value) =>
        AssetKey.From(value).Value.ShouldBe(value);

    [Theory]
    [InlineData("note.txt")]
    [InlineData("folder/logo")]
    [InlineData("with space")]
    [InlineData("café")]
    [InlineData("")]
    [InlineData("  ")]
    public void An_asset_key_rejects_anything_else(string value) =>
        AssetKey.TryFrom(value, out _).ShouldBeFalse();

    [Fact]
    public void An_asset_key_is_at_most_256_characters()
    {
        AssetKey.TryFrom(new string('a', 256), out _).ShouldBeTrue();
        AssetKey.TryFrom(new string('a', 257), out _).ShouldBeFalse();
    }

    [Fact]
    public void An_invalid_asset_key_throws_from_From_and_reports_from_TryFrom()
    {
        Should.Throw<ValueObjectValidationException>(() => AssetKey.From("note.txt"));
        AssetKey.TryFrom("note.txt", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_implicit_string_conversion_validates()
    {
        AssetKey key = "note_1";
        key.Value.ShouldBe("note_1");

        Should.Throw<ValueObjectValidationException>(() => Convert("note.txt"));

        static AssetKey Convert(string value) => value;
    }

    [Fact]
    public void An_asset_key_is_the_id_of_the_entry_the_upload_creates() =>
        AssetKey.From("note_1").AsEntryId().Value.ShouldBe("note_1");

    [Theory]
    [InlineData("logo.png")]
    [InlineData("a report (final).pdf")]
    [InlineData("café.txt")]
    public void A_filename_accepts_a_plain_file_name(string value) =>
        AssetFilename.From(value).Value.ShouldBe(value);

    [Theory]
    [InlineData("folder/logo.png")]
    [InlineData("folder\\logo.png")]
    [InlineData("logo\u0001.png")]
    [InlineData(" logo.png")]
    [InlineData(".logo")]
    [InlineData("logo.png ")]
    [InlineData("logo.")]
    [InlineData("logo..png")]
    [InlineData("")]
    public void A_filename_rejects_what_the_server_rejects(string value) =>
        AssetFilename.TryFrom(value, out _).ShouldBeFalse();

    [Fact]
    public void A_filename_is_at_most_200_characters()
    {
        AssetFilename.TryFrom(new string('a', 200), out _).ShouldBeTrue();
        AssetFilename.TryFrom(new string('a', 201), out _).ShouldBeFalse();
    }

    [Fact]
    public void A_rejected_filename_names_the_rule_it_broke() =>
        Should.Throw<ValueObjectValidationException>(() => AssetFilename.From("folder/logo.png"))
            .Message.ShouldContain("path");

    [Fact]
    public void A_filename_can_be_taken_from_a_path() =>
        AssetFilename.FromPath(Path.Combine("files", "logo.png")).Value.ShouldBe("logo.png");

    [Fact]
    public void A_folder_id_rejects_an_empty_guid()
    {
        FolderId.TryFrom(Guid.Empty, out _).ShouldBeFalse();
        FolderId.TryFrom(Guid.Parse("2f1d4a3c-0000-4000-8000-000000000001"), out _).ShouldBeTrue();
    }
}
