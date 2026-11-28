using CSharpFunctionalExtensions;
using Occtoo.Cards.Internal;
using Occtoo.Events;
using Occtoo.Http.Internal;

namespace Occtoo.Cards;

/// <summary>
/// Searches the entries of card definitions. Reached through
/// <see cref="OcctooClient.CardEntries"/>; requires
/// <see cref="Authentication.OcctooScopes.ReadCards"/>.
/// </summary>
/// <remarks>
/// An activation of the card definition invalidates cursors. A search
/// response over 8 MiB is refused: select fewer properties or take fewer entries.
/// </remarks>
public sealed class CardEntriesClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal CardEntriesClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of a card definition's entries, with the facets asked for.</summary>
    public Task<Result<CardSearchResult, OcctooError>> Search(
        CardDefinitionId cardDefinitionId,
        CardSearch search,
        CancellationToken cancellationToken = default) =>
        Send($"v1/card-definitions/{Uri.EscapeDataString(cardDefinitionId.Value)}/entries/search", search, "search card entries",
            cardDefinitionId, cancellationToken);

    /// <summary>
    /// Reads one page of the entries related to one card entry through a
    /// relation definition.
    /// </summary>
    public Task<Result<CardSearchResult, OcctooError>> SearchRelated(
        CardDefinitionId cardDefinitionId,
        CardId entryId,
        RelationDefinitionId relationDefinitionId,
        CardSearch search,
        CancellationToken cancellationToken = default) =>
        Send(
            $"v1/card-definitions/{Uri.EscapeDataString(cardDefinitionId.Value)}/entries/{Uri.EscapeDataString(entryId.Value)}"
            + $"/relations/{Uri.EscapeDataString(relationDefinitionId.Value)}/search",
            search, "search related card entries", cardDefinitionId, cancellationToken);

    private Task<Result<CardSearchResult, OcctooError>> Send(
        string route, CardSearch search, string operation, CardDefinitionId cardDefinitionId, CancellationToken cancellationToken)
    {
        if (search is null)
            return Task.FromResult(Result.Failure<CardSearchResult, OcctooError>(new ValidationError("A search is required.")));

        var request = OcctooTransport.Request(HttpMethod.Post, new Uri(route, UriKind.Relative),
            SearchCardEntriesDto.From(search), CardEntriesJsonContext.Default.SearchCardEntriesDto);

        // A search changes nothing, so it is safe to resend after a transient failure.
        request.Options.Set(OcctooResilience.Replayable, true);

        return OcctooTransport.Send(_httpClient, _requestTimeout, request, operation, CardEntriesJsonContext.Default.CardEntriesDto,
                cancellationToken, [new("occtoo.card_definition.id", cardDefinitionId.Value), new("occtoo.page.limit", search.Limit)])
            .MapResponse(dto => dto.ToModel());
    }
}
