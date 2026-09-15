using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Http.Internal;
using Occtoo.Logging;
using Occtoo.ManagedTags.Internal;

namespace Occtoo.ManagedTags;

/// <summary>
/// The Managed tags feature — the tenant's managed tags and their values.
/// Reached through <see cref="OcctooClient.ManagedTags"/>.
/// </summary>
/// <remarks>
/// Reads require <see cref="Authentication.OcctooScopes.ReadCards"/> and
/// writes <see cref="Authentication.OcctooScopes.WriteCards"/> — the
/// tenant-wide capabilities that also cover cards and card definitions.
/// </remarks>
public sealed class ManagedTagsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal ManagedTagsClient(HttpClient httpClient, ILogger logger, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    // ── Tags ───────────────────────────────────────────────────────────────

    /// <summary>Reads one page of managed tags.</summary>
    public Task<Result<Page<ManagedTag>, OcctooError>> List(
        ManagedTagListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ManagedTagListQuery();
        if (Pages.Validate(query.Page) is { HasValue: true } invalid)
            return Task.FromResult(Result.Failure<Page<ManagedTag>, OcctooError>(invalid.Value));

        var uri = new QueryString("v1/managed-tags")
            .Add("name", query.Name)
            .AddEnum("type", query.Type)
            .Add("parentId", query.ParentId.Map(id => id.Value))
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add("after", query.Page.After)
            .Add("limit", query.Page.Limit)
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "list managed tags",
                ManagedTagsJsonContext.Default.ForwardPageDtoManagedTagDto, cancellationToken)
            .Map(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }


    /// <summary>Reads one managed tag.</summary>
    public Task<Result<ManagedTag, OcctooError>> Get(
        ManagedTagId managedTagId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, TagUri(managedTagId)),
                "get managed tag", ManagedTagsJsonContext.Default.ManagedTagDto, cancellationToken)
            .Map(dto => dto.ToModel());

    /// <summary>Creates a managed tag.</summary>
    public Task<Result<ManagedTag, OcctooError>> Create(
        CreateManagedTag tag,
        CancellationToken cancellationToken = default)
    {
        if (tag is null)
            return Task.FromResult(Result.Failure<ManagedTag, OcctooError>(new ValidationError("A managed tag is required.")));

        var body = new CreateManagedTagDto(tag.DisplayName, tag.Type, tag.ParentId.HasValue ? tag.ParentId.Value.Value : null);
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri("v1/managed-tags", UriKind.Relative), body,
                    ManagedTagsJsonContext.Default.CreateManagedTagDto),
                "create managed tag", ManagedTagsJsonContext.Default.ManagedTagDto, cancellationToken)
            .Map(dto => dto.ToModel())
            .Tap(created => OcctooLog.ManagedTagCreated(_logger, created.Id.Value, created.DisplayName));
    }

    /// <summary>Replaces a managed tag's name and parent.</summary>
    public Task<Result<ManagedTag, OcctooError>> Update(
        ManagedTagId managedTagId,
        UpdateManagedTag tag,
        CancellationToken cancellationToken = default)
    {
        if (tag is null)
            return Task.FromResult(Result.Failure<ManagedTag, OcctooError>(new ValidationError("A managed tag is required.")));

        var body = new UpdateManagedTagDto(tag.DisplayName, tag.ParentId.HasValue ? tag.ParentId.Value.Value : null);
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Put, TagUri(managedTagId), body, ManagedTagsJsonContext.Default.UpdateManagedTagDto),
                "update managed tag", ManagedTagsJsonContext.Default.ManagedTagDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>
    /// Deletes a managed tag and its values, detaching nested tags. A tag a
    /// card definition still references is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        ManagedTagId managedTagId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, TagUri(managedTagId)), "delete managed tag", cancellationToken)
            .Tap(() => OcctooLog.ManagedTagDeleted(_logger, managedTagId.Value));

    // ── Values ─────────────────────────────────────────────────────────────

    /// <summary>Reads one page of a managed tag's values.</summary>
    public Task<Result<Page<ManagedTagValue>, OcctooError>> ListValues(
        ManagedTagId managedTagId,
        ManagedTagValueListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ManagedTagValueListQuery();
        if (Pages.Validate(query.Page) is { HasValue: true } invalid)
            return Task.FromResult(Result.Failure<Page<ManagedTagValue>, OcctooError>(invalid.Value));

        var uri = new QueryString($"v1/managed-tags/{managedTagId.Value:D}/values")
            .Add("key", query.Key)
            .Add("parentKey", query.ParentKey.Map(key => key.Value))
            .Add("after", query.Page.After)
            .Add("limit", query.Page.Limit)
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "list managed tag values",
                ManagedTagsJsonContext.Default.ForwardPageDtoManagedTagValueDto, cancellationToken)
            .Map(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }


    /// <summary>Reads one value.</summary>
    public Task<Result<ManagedTagValue, OcctooError>> GetValue(
        ManagedTagId managedTagId,
        ManagedTagValueKey key,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, ValueUri(managedTagId, key)),
                "get managed tag value", ManagedTagsJsonContext.Default.ManagedTagValueDto, cancellationToken)
            .Map(dto => dto.ToModel());

    /// <summary>Creates a value. An existing key is a <see cref="ConflictError"/>.</summary>
    public Task<Result<ManagedTagValue, OcctooError>> CreateValue(
        ManagedTagId managedTagId,
        CreateManagedTagValue value,
        CancellationToken cancellationToken = default)
    {
        if (value is null)
            return Task.FromResult(Result.Failure<ManagedTagValue, OcctooError>(new ValidationError("A value is required.")));

        var body = new CreateManagedTagValueDto(
            value.Key.Value,
            ManagedTagValueContentDto.From(value.Content),
            value.Order,
            value.ParentKey.Map(key => key.Value).GetValueOrDefault());

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri($"v1/managed-tags/{managedTagId.Value:D}/values", UriKind.Relative),
                    body, ManagedTagsJsonContext.Default.CreateManagedTagValueDto),
                "create managed tag value", ManagedTagsJsonContext.Default.ManagedTagValueDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>Replaces a value's content, order, and parent.</summary>
    public Task<Result<ManagedTagValue, OcctooError>> UpdateValue(
        ManagedTagId managedTagId,
        ManagedTagValueKey key,
        UpdateManagedTagValue value,
        CancellationToken cancellationToken = default)
    {
        if (value is null)
            return Task.FromResult(Result.Failure<ManagedTagValue, OcctooError>(new ValidationError("A value is required.")));

        var body = new UpdateManagedTagValueDto(
            ManagedTagValueContentDto.From(value.Content),
            value.Order,
            value.ParentKey.Map(parent => parent.Value).GetValueOrDefault());

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Put, ValueUri(managedTagId, key), body,
                    ManagedTagsJsonContext.Default.UpdateManagedTagValueDto),
                "update managed tag value", ManagedTagsJsonContext.Default.ManagedTagValueDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>Deletes a value, clearing parent links to it within its tag.</summary>
    public Task<UnitResult<OcctooError>> DeleteValue(
        ManagedTagId managedTagId,
        ManagedTagValueKey key,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, ValueUri(managedTagId, key)), "delete managed tag value", cancellationToken);

    private static Uri TagUri(ManagedTagId managedTagId) =>
        new($"v1/managed-tags/{managedTagId.Value:D}", UriKind.Relative);

    private static Uri ValueUri(ManagedTagId managedTagId, ManagedTagValueKey key) =>
        new($"v1/managed-tags/{managedTagId.Value:D}/values/{Uri.EscapeDataString(key.Value)}", UriKind.Relative);
}
