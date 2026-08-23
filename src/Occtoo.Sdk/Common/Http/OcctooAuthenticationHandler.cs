using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Occtoo.Authentication;
using Occtoo.Logging;

namespace Occtoo.Http;

/// <summary>
/// Applies the configured credential to every outgoing request, and recovers
/// from a token that Occtoo rejects before its stated expiry.
/// </summary>
/// <remarks>
/// Register this in an <see cref="IHttpClientFactory"/> pipeline to authenticate
/// requests the SDK does not make itself. A credential that cannot be
/// established surfaces as an <see cref="OcctooCredentialException"/>, which the
/// SDK's own surfaces convert back into a failed result.
/// </remarks>
public sealed class OcctooAuthenticationHandler : DelegatingHandler
{
    private readonly IOcctooCredential _credential;
    private readonly bool _retryOnUnauthorized;
    private readonly ILogger _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="credential">The credential to apply.</param>
    /// <param name="retryOnUnauthorized">
    /// Whether to discard the cached token and retry once on
    /// <c>401 Unauthorized</c>.
    /// </param>
    /// <param name="logger">
    /// Where the handler reports, under the
    /// <see cref="OcctooLogCategories.Http"/> category. Optional.
    /// </param>
    public OcctooAuthenticationHandler(
        IOcctooCredential credential,
        bool retryOnUnauthorized = true,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
        _retryOnUnauthorized = retryOnUnauthorized;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var canRetry = _retryOnUnauthorized && _credential is OcctooTokenCredential;

        // A retry re-sends this request, and content is not generally readable
        // twice. Buffer it up front so the second send has something to write.
        if (canRetry && request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        await Apply(request, cancellationToken).ConfigureAwait(false);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized ||
            _credential is not OcctooTokenCredential tokenCredential ||
            !canRetry)
        {
            return response;
        }

        // The cached token was accepted at issue time and is not yet expired, so
        // it has been revoked or rotated. Nothing about retrying with the same
        // token would help; get a new one. Invalidate only the token this
        // request actually sent, so concurrent 401s refresh once, not N times.
        OcctooLog.RetryingRejectedToken(_logger);
        response.Dispose();
        var rejectedToken = request.Headers.Authorization?.Parameter ?? "";
        await tokenCredential.InvalidateIfCurrent(rejectedToken, cancellationToken).ConfigureAwait(false);
        await Apply(request, cancellationToken).ConfigureAwait(false);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task Apply(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var applied = await _credential.Apply(request, cancellationToken).ConfigureAwait(false);
        if (applied.IsFailure)
            throw new OcctooCredentialException(applied.Error);
    }
}
