using Vogen;

namespace Occtoo.Cards;

// Card identifiers. Constructed ids follow the API's rule — a lowercase ASCII
// letter, then ASCII letters and digits — and are case-sensitive; ids read
// back from Occtoo are kept as stored. Card definition, card, segment and
// segment definition ids are shared with the Events feature (Occtoo.Events).

/// <summary>
/// The id of a card property, such as <c>name</c> or <c>parentSku</c>, or
/// <c>id</c> for the card's own id. Case-sensitive, unlike a source
/// <see cref="Sources.PropertyId"/>.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct CardPropertyId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator CardPropertyId(string value) => From(value);

    private static Validation Validate(string input) => CardIds.Validate(input, "A card property id");

#pragma warning disable VOG010 // ids read back from Occtoo are kept as stored
    internal static CardPropertyId Read(string value) => new(value);
#pragma warning restore VOG010
}

/// <summary>The id of a relation definition, such as <c>productVariants</c>.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct RelationDefinitionId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator RelationDefinitionId(string value) => From(value);

    private static Validation Validate(string input) => CardIds.Validate(input, "A relation definition id");

#pragma warning disable VOG010 // ids read back from Occtoo are kept as stored
    internal static RelationDefinitionId Read(string value) => new(value);
#pragma warning restore VOG010
}

internal static class CardIds
{
    internal static Validation Validate(string input, string what) =>
        !string.IsNullOrEmpty(input) && char.IsAsciiLetterLower(input[0]) && input.All(char.IsAsciiLetterOrDigit)
            ? Validation.Ok
            : Validation.Invalid($"{what} must start with a lowercase letter, then letters and digits.");
}
