using System.Diagnostics;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Assets.Internal;
using Occtoo.Http.Internal;
using Occtoo.Logging;
using Occtoo.Sources;
using Occtoo.Telemetry;

namespace Occtoo.Assets;

/// <summary>
/// The asset surface — <c>/v1/assets/{dataSourceId}</c>. Reached through
/// <see cref="OcctooClient.Assets"/>.
/// </summary>
/// <remarks>
/// <para>
/// An asset is a file stored in a Media data source: Occtoo creates the entry
/// and signs a link, the bytes go straight to blob storage, and Occtoo then
/// reads the blob back and makes a file of it. <see cref="Upload"/> does all
/// three and is the call to reach for; the steps underneath it are public for
/// callers who have to drive them separately — a link handed to another
/// process, a transfer that happens elsewhere, an upload resumed after a
/// restart.
/// </para>
/// <para>
/// The target must be a Media data source, and the credential needs one of
/// <see cref="Authentication.OcctooScopes.WriteSources"/>,
/// <see cref="Authentication.OcctooScopes.WriteMedia"/> or
/// <c>import-datasource</c>.
/// </para>
/// </remarks>
public sealed partial class AssetsClient
{
    /// <summary>The most assets Occtoo initializes in one call.</summary>
    public const int MaxAssetsPerInitialize = 100;

    /// <summary>
    /// The most assets Occtoo completes in one call — half what it initializes,
    /// which is why <see cref="Upload"/> completes a large run in several
    /// calls.
    /// </summary>
    public const int MaxAssetsPerCompletion = 50;

    /// <summary>
    /// The most assets Occtoo reads, deletes or re-signs links for in one call.
    /// </summary>
    public const int MaxKeysPerRequest = 50;

    /// <summary>
    /// The most bytes a single upload can carry. Storage accepts no more in one
    /// request, and discovering that after transferring gigabytes is the most
    /// expensive way to learn it.
    /// </summary>
    public const long MaxContentLength = 5000L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly Lazy<HttpClient> _uploadHttpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal AssetsClient(
        HttpClient httpClient,
        Lazy<HttpClient> uploadHttpClient,
        ILogger logger,
        TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _uploadHttpClient = uploadHttpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    /// <summary>
    /// Creates the assets and signs a link to upload each one's bytes to.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="assets">The assets to create. At least one, at most <see cref="MaxAssetsPerInitialize"/>, with distinct keys.</param>
    /// <param name="folderId">The folder the entries land in; the root folder when omitted.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// A link per asset that was created, and Occtoo's reason for each one that
    /// was not. Initializing an asset that already exists signs a fresh link
    /// for it.
    /// </returns>
    public Task<Result<AssetBatch<AssetUploadLink>, OcctooError>> Initialize(
        SourceId dataSourceId,
        IReadOnlyCollection<Asset> assets,
        Maybe<FolderId> folderId = default,
        CancellationToken cancellationToken = default) =>
        Validate(assets, MaxAssetsPerInitialize, "initialized")
            .Tap(() => OcctooLog.InitializingAssets(_logger, assets.Count, dataSourceId.Value))
            .Bind(() => SendForLinks(
                OcctooTransport.Request(
                    HttpMethod.Post,
                    AssetsUri(dataSourceId),
                    new InitializeAssetsRequestDto(
                        folderId.HasValue ? folderId.Value.Value : (Guid?)null,
                        ToDtos(assets)),
                    AssetsJsonContext.Default.InitializeAssetsRequestDto),
                assets,
                "initialize assets",
                dataSourceId,
                folderId,
                cancellationToken));

    /// <summary>
    /// Signs a fresh link for assets that are already initialized — for a link
    /// that expired before its bytes were sent, or never reached the process
    /// that needed it.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="assets">The assets to re-sign. At least one, at most <see cref="MaxKeysPerRequest"/>, with distinct keys.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// A link per asset that could be re-signed, and Occtoo's reason for each
    /// one that could not. An asset that was never initialized, or that is
    /// already completed, is refused — delete a completed asset and initialize
    /// it again to replace the file.
    /// </returns>
    public Task<Result<AssetBatch<AssetUploadLink>, OcctooError>> RefreshUploadLinks(
        SourceId dataSourceId,
        IReadOnlyCollection<Asset> assets,
        CancellationToken cancellationToken = default) =>
        Validate(assets, MaxKeysPerRequest, "re-signed")
            .Bind(() => SendForLinks(
                OcctooTransport.Request(
                    HttpMethod.Post,
                    AssetsUri(dataSourceId, "uploadLinks"),
                    new AssetsRequestDto(ToDtos(assets)),
                    AssetsJsonContext.Default.AssetsRequestDto),
                assets,
                "refresh asset upload links",
                dataSourceId,
                Maybe<FolderId>.None,
                cancellationToken));

    /// <summary>
    /// Sends one asset's bytes to the link Occtoo signed for it.
    /// </summary>
    /// <param name="link">The link to upload to.</param>
    /// <param name="content">The bytes to send.</param>
    /// <param name="options">How to run the transfer; the defaults are fine.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>
    /// What was moved, or why it was not. The request goes to blob storage
    /// rather than to Occtoo, so its failures name storage's own error code and
    /// request id; a link that has expired is an
    /// <see cref="AuthenticationError"/>, and
    /// <see cref="RefreshUploadLinks"/> is what fixes it.
    /// </returns>
    /// <remarks>
    /// The transfer is a single request and cannot be resumed. The file's mime
    /// type is not decided here — Occtoo reads it off the blob during
    /// <see cref="Complete"/>.
    /// </remarks>
    public async Task<Result<AssetTransfer, OcctooError>> Transfer(
        AssetUploadLink link,
        AssetContent content,
        AssetTransferOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (link is null)
            return new ValidationError("An upload link is required.");

        if (content is null)
            return new ValidationError("Content is required.");

        options ??= new AssetTransferOptions();

        using var activity = OcctooTelemetry.Source.StartActivity("transfer asset", ActivityKind.Client);
        activity?.SetTag("occtoo.assets.key", link.Key.Value);

        var outcome = await ResolveLength(content)
            .Match(
                length => TransferBytes(link, content, length, options, cancellationToken),
                error => Task.FromResult(Result.Failure<AssetTransfer, OcctooError>(error)))
            .ConfigureAwait(false);

        return outcome
            .Tap(transfer => activity?.SetTag("occtoo.assets.bytes", transfer.Bytes))
            .TapError(error => OcctooTelemetry.Fail(activity, error));
    }

    /// <summary>
    /// Tells Occtoo the bytes are in place, so it can read each blob back and
    /// create the file.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="assets">The assets to complete, with the same filenames they were initialized under. At least one, at most <see cref="MaxAssetsPerCompletion"/>, with distinct keys.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The file Occtoo created per asset, and its reason for each asset it
    /// could not complete. Completion is per asset, so one missing blob does
    /// not sink the batch.
    /// </returns>
    public Task<Result<AssetBatch<AssetFileInfo>, OcctooError>> Complete(
        SourceId dataSourceId,
        IReadOnlyCollection<Asset> assets,
        CancellationToken cancellationToken = default) =>
        Validate(assets, MaxAssetsPerCompletion, "completed")
            .Bind(() => OcctooTransport
                .Send(
                    _httpClient,
                    _requestTimeout,
                    OcctooTransport.Request(
                        HttpMethod.Post,
                        AssetsUri(dataSourceId, "complete"),
                        new AssetsRequestDto(ToDtos(assets)),
                        AssetsJsonContext.Default.AssetsRequestDto),
                    "complete assets",
                    AssetsJsonContext.Default.CompletedAssetsDto,
                    cancellationToken,
                    Tags(dataSourceId, assets.Count),
                    dto =>
                    [
                        new("occtoo.assets.completed", dto.Completed?.Count ?? 0),
                        new("occtoo.assets.failed", dto.Failed?.Count ?? 0),
                    ])
                .Bind(ToFiles)
                .Tap(LogRejections));

    /// <summary>
    /// Reads what Occtoo knows about assets it was given keys for.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="keys">The keys to read. At least one, at most <see cref="MaxKeysPerRequest"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One entry per key Occtoo knows. A key that was never initialized is
    /// absent from the dictionary rather than reported as a state of its own.
    /// </returns>
    /// <remarks>
    /// The status is updated from an event, so it trails a successful
    /// <see cref="Complete"/> — an asset that has just been completed can still
    /// read as <see cref="AssetStatus.Initialized"/> here. Use this to recover
    /// state, not to confirm an upload that just finished.
    /// </remarks>
    public Task<Result<IReadOnlyDictionary<AssetKey, AssetState>, OcctooError>> GetState(
        SourceId dataSourceId,
        IReadOnlyCollection<AssetKey> keys,
        CancellationToken cancellationToken = default) =>
        ValidateKeys(keys, "read")
            .Bind(() => OcctooTransport
                .Send(
                    _httpClient,
                    _requestTimeout,
                    OcctooTransport.Request(HttpMethod.Get, KeysUri(dataSourceId, keys)),
                    "read asset states",
                    AssetsJsonContext.Default.DictionaryStringAssetStateDto,
                    cancellationToken,
                    Tags(dataSourceId, keys.Count),
                    states => [new("occtoo.assets.found", states.Count)])
                .Bind(ToStates));

    /// <summary>
    /// Deletes assets and the files behind them.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="keys">The keys to delete. At least one, at most <see cref="MaxKeysPerRequest"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Nothing, or why the call was refused. Deleting is how an asset's file is
    /// replaced: initialize the same key again afterwards.
    /// </returns>
    /// <remarks>
    /// The entries go first and the files follow, so a file can outlive its
    /// entry by a moment.
    /// </remarks>
    public Task<UnitResult<OcctooError>> Delete(
        SourceId dataSourceId,
        IReadOnlyCollection<AssetKey> keys,
        CancellationToken cancellationToken = default) =>
        ValidateKeys(keys, "deleted")
            .Bind(() => OcctooTransport.Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(HttpMethod.Delete, KeysUri(dataSourceId, keys)),
                "delete assets",
                cancellationToken,
                Tags(dataSourceId, keys.Count)));

    private async Task<Result<AssetTransfer, OcctooError>> TransferBytes(
        AssetUploadLink link,
        AssetContent content,
        long length,
        AssetTransferOptions options,
        CancellationToken cancellationToken)
    {
        var transferred = await BlobTransfer
            .Put(_uploadHttpClient.Value, link, content, length, options, cancellationToken)
            .ConfigureAwait(false);

        return transferred.MapError(failure => failure.Error);
    }

    private Task<Result<AssetBatch<AssetUploadLink>, OcctooError>> SendForLinks(
        HttpRequestMessage request,
        IReadOnlyCollection<Asset> assets,
        string operation,
        SourceId dataSourceId,
        Maybe<FolderId> folderId,
        CancellationToken cancellationToken) =>
        OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                request,
                operation,
                AssetsJsonContext.Default.InitializedAssetsDto,
                cancellationToken,
                Tags(dataSourceId, assets.Count, folderId),
                dto =>
                [
                    new("occtoo.assets.signed", dto.Initialized?.Count ?? 0),
                    new("occtoo.assets.refused", dto.Failed?.Count ?? 0),
                ])
            .Bind(initialized => ToLinks(initialized, assets))
            .Tap(LogRejections);

    private void LogRejections<T>(AssetBatch<T> batch)
        where T : notnull
    {
        foreach (var (key, reason) in batch.Failed)
            OcctooLog.AssetRejected(_logger, key.Value, reason);
    }

    private static Result<long, OcctooError> ResolveLength(AssetContent content) =>
        content.ResolveLength().Bind(Result<long, OcctooError> (length) => length switch
        {
            < 0 => new ValidationError(
                $"The content states a length of {length} bytes, but a length cannot be negative."),
            > MaxContentLength => new ValidationError(
                $"The content is {length} bytes. One upload carries at most {MaxContentLength} bytes "
                + $"({MaxContentLength / (1024 * 1024)} MiB)."),
            _ => length,
        });

    private static Result<AssetBatch<AssetUploadLink>, OcctooError> ToLinks(
        InitializedAssetsDto dto,
        IReadOnlyCollection<Asset> assets)
    {
        var requested = assets.ToDictionary(asset => asset.Key.Value);
        var signed = new Dictionary<AssetKey, AssetUploadLink>();

        foreach (var (key, initialization) in dto.Initialized ?? [])
        {
            if (!requested.TryGetValue(key, out var asset))
            {
                return new UnexpectedError(
                    $"Occtoo returned an upload link for '{key}', which was not part of the request.");
            }

            if (initialization.UploadUrl is not { Length: > 0 } url
                || !Uri.TryCreate(url, UriKind.Absolute, out var location))
            {
                return new UnexpectedError($"Occtoo returned an upload link for '{key}' that is not a URL.");
            }

            signed[asset.Key] = new AssetUploadLink(asset.Key, asset.Filename, location, initialization.ExpiresAt);
        }

        return ToBatch(signed, dto.Failed);
    }

    private static Result<AssetBatch<AssetFileInfo>, OcctooError> ToFiles(CompletedAssetsDto dto)
    {
        var completed = new Dictionary<AssetKey, AssetFileInfo>();

        foreach (var (key, file) in dto.Completed ?? [])
        {
            if (!AssetKey.TryFrom(key, out var parsed))
                return UnknownKey(key);

            if (!AssetFilename.TryFrom(file.Filename ?? "", out var filename))
                return new UnexpectedError($"Occtoo completed '{key}' under a filename the SDK cannot read.");

            if (file.PublicUrl is not { Length: > 0 } url
                || !Uri.TryCreate(url, UriKind.Absolute, out var publicUrl))
            {
                return new UnexpectedError($"Occtoo completed '{key}' with a public URL the SDK cannot read.");
            }

            completed[parsed] = new AssetFileInfo(
                filename,
                file.MimeType ?? "",
                file.Size,
                publicUrl,
                file.Width.HasValue ? Maybe.From(file.Width.Value) : Maybe<int>.None,
                file.Height.HasValue ? Maybe.From(file.Height.Value) : Maybe<int>.None);
        }

        return ToBatch(completed, dto.Failed);
    }

    private static Result<IReadOnlyDictionary<AssetKey, AssetState>, OcctooError> ToStates(
        Dictionary<string, AssetStateDto> dto)
    {
        var states = new Dictionary<AssetKey, AssetState>();

        foreach (var (key, state) in dto)
        {
            if (!AssetKey.TryFrom(key, out var parsed))
                return UnknownKey(key);

            states[parsed] = new AssetState(
                Enums.Read<AssetStatus>(state.Status),
                AssetFilename.TryFrom(state.Filename ?? "", out var filename)
                    ? Maybe.From(filename)
                    : Maybe<AssetFilename>.None,
                state.MimeType is { Length: > 0 } mimeType ? Maybe.From(mimeType) : Maybe<string>.None,
                state.Size.HasValue ? Maybe.From(state.Size.Value) : Maybe<long>.None,
                state.PublicUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var publicUrl)
                    ? Maybe.From(publicUrl)
                    : Maybe<Uri>.None);
        }

        return Result.Success<IReadOnlyDictionary<AssetKey, AssetState>, OcctooError>(states);
    }

    private static Result<AssetBatch<T>, OcctooError> ToBatch<T>(
        Dictionary<AssetKey, T> succeeded,
        Dictionary<string, string>? failed)
        where T : notnull
    {
        var rejected = new Dictionary<AssetKey, string>();

        foreach (var (key, reason) in failed ?? [])
        {
            if (!AssetKey.TryFrom(key, out var parsed))
                return Result.Failure<AssetBatch<T>, OcctooError>(UnknownKey(key));

            rejected[parsed] = reason;
        }

        return Result.Success<AssetBatch<T>, OcctooError>(new(succeeded, rejected));
    }

    private static UnexpectedError UnknownKey(string key) =>
        new($"Occtoo answered about the asset key '{key}', which is not a key this SDK can represent.");

    private static List<AssetDto> ToDtos(IReadOnlyCollection<Asset> assets) =>
        [.. assets.Select(asset => new AssetDto(asset.Key.Value, asset.Filename.Value))];

    private static List<KeyValuePair<string, object?>> Tags(
        SourceId dataSourceId,
        int count,
        Maybe<FolderId> folderId = default)
    {
        List<KeyValuePair<string, object?>> tags =
        [
            new("occtoo.source.id", dataSourceId.Value),
            new("occtoo.assets.count", count),
        ];

        if (folderId.HasValue)
            tags.Add(new("occtoo.folder.id", folderId.Value.Value));

        return tags;
    }

    private static Uri AssetsUri(SourceId dataSourceId) =>
        new($"v1/assets/{Uri.EscapeDataString(dataSourceId.Value)}", UriKind.Relative);

    private static Uri AssetsUri(SourceId dataSourceId, string operation) =>
        new($"v1/assets/{Uri.EscapeDataString(dataSourceId.Value)}/{operation}", UriKind.Relative);

    private static Uri KeysUri(SourceId dataSourceId, IReadOnlyCollection<AssetKey> keys)
    {
        var builder = new StringBuilder("v1/assets/").Append(Uri.EscapeDataString(dataSourceId.Value));
        var separator = '?';

        foreach (var key in keys)
        {
            builder.Append(separator).Append("key=").Append(Uri.EscapeDataString(key.Value));
            separator = '&';
        }

        return new Uri(builder.ToString(), UriKind.Relative);
    }

    // The ceilings are contract limits a validator enforces, so the SDK rejects
    // an oversized batch where the caller made it rather than spending a round
    // trip to be told the same thing.
    private static UnitResult<OcctooError> Validate(
        IReadOnlyCollection<Asset>? assets,
        int ceiling,
        string operation) =>
        assets switch
        {
            null or { Count: 0 } => new ValidationError("At least one asset is required."),
            not null when assets.Count > ceiling =>
                new ValidationError($"At most {ceiling} assets can be {operation} per call."),
            not null when HasDuplicates(
                [.. assets.Select(asset => asset.Key.Value)], StringComparer.OrdinalIgnoreCase) =>
                new ValidationError("Asset keys must be distinct within a call; Occtoo compares them ignoring case."),
            _ => UnitResult.Success<OcctooError>(),
        };

    // The key lists compare exactly, because that is how Occtoo compares them
    // here: rejecting a pair that differs only in case would refuse a call the
    // server would have accepted.
    private static UnitResult<OcctooError> ValidateKeys(IReadOnlyCollection<AssetKey>? keys, string operation) =>
        keys switch
        {
            null or { Count: 0 } => new ValidationError("At least one asset key is required."),
            { Count: > MaxKeysPerRequest } =>
                new ValidationError($"At most {MaxKeysPerRequest} assets can be {operation} per call."),
            not null when HasDuplicates([.. keys.Select(key => key.Value)], StringComparer.Ordinal) =>
                new ValidationError("Asset keys must be distinct within a call."),
            _ => UnitResult.Success<OcctooError>(),
        };

    private static bool HasDuplicates(IReadOnlyCollection<string> keys, StringComparer comparison)
    {
        var seen = new HashSet<string>(comparison);
        return keys.Any(key => !seen.Add(key));
    }
}
