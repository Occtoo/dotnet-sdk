using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;

namespace Occtoo.Authentication.Internal;

/// <summary>
/// The one place that talks to an OAuth 2.0 token endpoint, so every flow reports
/// failures the same way.
/// </summary>
internal static class OAuthTokenEndpoint
{
    /// <summary>
    /// Posts a form-encoded grant and returns the parsed response. A rejection
    /// comes back as an <see cref="AuthenticationError"/> carrying the OAuth
    /// <c>error</c> code, so polling flows can act on
    /// <c>authorization_pending</c> and <c>slow_down</c>.
    /// </summary>
    internal static async Task<Result<TokenResponse, OcctooError>> PostGrant(
        HttpClient httpClient,
        Uri endpoint,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        var sent = await Send(httpClient, endpoint, parameters, cancellationToken).ConfigureAwait(false);
        if (sent.IsFailure)
            return sent.ConvertFailure<TokenResponse>();

        using var response = sent.Value;

        if (response.IsSuccessStatusCode)
        {
            var token = await ReadJson(
                response,
                OAuthJsonContext.Default.TokenResponse,
                cancellationToken).ConfigureAwait(false);

            return token is { AccessToken.Length: > 0 }
                ? token
                : new UnexpectedError(
                    $"The authorization server at {endpoint} returned a response with no access token.",
                    (int)response.StatusCode);
        }

        var error = await ReadJson(
            response,
            OAuthJsonContext.Default.OAuthErrorResponse,
            cancellationToken).ConfigureAwait(false);

        return Classify("The token request was rejected", endpoint, response, error);
    }

    /// <summary>
    /// Posts a device authorization request and returns the device code to show
    /// the user.
    /// </summary>
    internal static async Task<Result<DeviceAuthorizationResponse, OcctooError>> PostDeviceAuthorization(
        HttpClient httpClient,
        Uri endpoint,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        var sent = await Send(httpClient, endpoint, parameters, cancellationToken).ConfigureAwait(false);
        if (sent.IsFailure)
            return sent.ConvertFailure<DeviceAuthorizationResponse>();

        using var response = sent.Value;

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadJson(
                response,
                OAuthJsonContext.Default.OAuthErrorResponse,
                cancellationToken).ConfigureAwait(false);

            return Classify("Device authorization was refused", endpoint, response, error);
        }

        var device = await ReadJson(
            response,
            OAuthJsonContext.Default.DeviceAuthorizationResponse,
            cancellationToken).ConfigureAwait(false);

        return device is { DeviceCode.Length: > 0, UserCode.Length: > 0, VerificationUri.Length: > 0 }
            ? device
            : new UnexpectedError(
                $"The device authorization endpoint at {endpoint} returned an incomplete response.",
                (int)response.StatusCode);
    }

    private static async Task<Result<HttpResponseMessage, OcctooError>> Send(
        HttpClient httpClient,
        Uri endpoint,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(parameters),
        };

        try
        {
            return await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return new NetworkError(
                $"Could not reach the authorization server at {endpoint}: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TimeoutError(
                $"The authorization server at {endpoint} did not answer in time.");
        }
    }

    private static OcctooError Classify(
        string prefix,
        Uri endpoint,
        HttpResponseMessage response,
        OAuthErrorResponse? error) => (response.StatusCode, error) switch
        {
            // Status outranks the error body: a 429/5xx is transient and
            // retryable even when it carries an OAuth error code (a 503 with
            // "temporarily_unavailable" must not read as "check the credential").
            (HttpStatusCode.TooManyRequests, _) => new RateLimitError(
                $"{prefix}: the authorization server at {endpoint} is rate limiting requests.",
                RetryAfter(response)),
            ( >= HttpStatusCode.InternalServerError, _) => new ServerError(
                $"{prefix}: the authorization server at {endpoint} responded {(int)response.StatusCode}.",
                (int)response.StatusCode),
            (_, { Error.Length: > 0 }) => new AuthenticationError(
                Describe(prefix, error, response.StatusCode),
                error.Error),
            _ => new AuthenticationError(Describe(prefix, error, response.StatusCode)),
        };

    internal static Maybe<TimeSpan> RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero
                ? wait
                : Maybe<TimeSpan>.None,
            _ => Maybe<TimeSpan>.None,
        };

    internal static string Describe(string prefix, OAuthErrorResponse? error, HttpStatusCode status) =>
        error switch
        {
            { Error.Length: > 0, ErrorDescription.Length: > 0 } => $"{prefix}: {error.Error} — {error.ErrorDescription}",
            { Error.Length: > 0 } => $"{prefix}: {error.Error}.",
            _ => $"{prefix}: the authorization server responded {(int)status}.",
        };

    private static async Task<T?> ReadJson<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync(typeInfo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A non-JSON body (a proxy error page, say) is not itself the failure
            // worth reporting — the caller reports the status instead.
            return default;
        }
        catch (NotSupportedException)
        {
            return default;
        }
    }
}
