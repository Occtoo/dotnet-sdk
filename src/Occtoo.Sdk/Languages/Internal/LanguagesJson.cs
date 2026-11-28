using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Languages.Internal;

internal sealed record LanguageDto(string Code, string? Name);

internal sealed record LanguagesDto(string? DefaultLanguage, LanguageDto[] Items)
{
    // Codes come back exactly as stored, valid ISO or not; see LanguageCode.Read.
    internal TenantLanguages ToModel() => new(
        LanguageCode.Read(DefaultLanguage),
        [.. Items.Select(item => new TenantLanguage(
            LanguageCode.Read(item.Code).GetValueOrThrow("A language came back without a code."),
            item.Name is { Length: > 0 } name ? name : Maybe<string>.None))]);

    internal static LanguagesDto From(TenantLanguages languages) => new(
        languages.Default.Map(code => code.Value).GetValueOrDefault(),
        [.. languages.Items.Select(language => new LanguageDto(language.Code.Value, language.Name.GetValueOrDefault()))]);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(LanguagesDto))]
internal sealed partial class LanguagesJsonContext : JsonSerializerContext;
