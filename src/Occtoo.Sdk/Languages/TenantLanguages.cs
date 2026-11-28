using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Languages;

/// <summary>
/// One language of the tenant. <c>Name</c> is absent for a language added
/// automatically from source entries.
/// </summary>
public sealed record TenantLanguage(LanguageCode Code, Maybe<string> Name);

/// <summary>
/// The tenant's languages and its default language, the one card exports and
/// enrichment use when none is given. Change it from what
/// <see cref="LanguagesClient.Get"/> returned and send it to
/// <see cref="LanguagesClient.Replace"/>: a language left out is removed.
/// </summary>
public sealed record TenantLanguages(Maybe<LanguageCode> Default, IReadOnlyList<TenantLanguage> Items)
{
    /// <summary>Adds a language, or renames it when the tenant already has it.</summary>
    public TenantLanguages With(LanguageCode code, Maybe<string> name = default) =>
        this with
        {
            Items = Items.Any(language => language.Code == code)
                ? [.. Items.Select(language => language.Code == code ? language with { Name = name } : language)]
                : [.. Items, new TenantLanguage(code, name)],
        };

    /// <summary>Removes a language, and clears the default when it was that one.</summary>
    public TenantLanguages Without(LanguageCode code) =>
        this with
        {
            Items = [.. Items.Where(language => language.Code != code)],
            Default = Default.Where(language => language != code),
        };

    /// <summary>Makes a language the default; it must be one of <c>Items</c>.</summary>
    public TenantLanguages WithDefault(LanguageCode code) => this with { Default = code };
}
