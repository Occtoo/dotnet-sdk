using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CSharpFunctionalExtensions;
using Occtoo.Telemetry;

namespace Occtoo.Http.Internal;

/// <summary>
/// The one place SDK surfaces send requests through, so every feature client
/// upholds the same contract: expected transport failures become results, the
/// per-request timeout is enforced here (the underlying
/// <see cref="HttpClient"/> runs with an infinite timeout so long-lived streams
/// survive), and only the caller's own cancellation propagates as an exception.
/// </summary>
/// <remarks>
/// The raw <see cref="Send(HttpClient, TimeSpan, HttpRequestMessage, CancellationToken, HttpCompletionOption)"/>
/// hands back the response for surfaces with bespoke handling (ingest's
/// streaming body, the SSE stream). The typed overloads add what every
/// resource operation needs on top: a span named after the operation,
/// API rejections classified into <see cref="OcctooError"/>, and a
/// source-generated JSON body — or no body, for 202/204 responses.
/// </remarks>
internal static class OcctooTransport
{
    internal static async Task<Result<HttpResponseMessage, OcctooError>> Send(
        HttpClient httpClient,
        TimeSpan timeout,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var linked = timeout != Timeout.InfiniteTimeSpan
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        linked?.CancelAfter(timeout);
        var token = linked?.Token ?? cancellationToken;

        try
        {
            return await httpClient.SendAsync(request, completion, token).ConfigureAwait(false);
        }
        catch (OcctooCredentialException exception)
        {
            return exception.Error;
        }
        catch (HttpRequestException exception)
        {
            return new NetworkError($"Could not reach Occtoo: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TimeoutError("Occtoo did not answer in time.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Handlers the consumer layered into the pipeline (a resilience
            // handler's timeout or circuit breaker, say) may throw their own
            // types. The no-throw contract holds at this boundary.
            return new UnexpectedError($"The request failed unexpectedly: {exception.Message}");
        }
    }

    /// <summary>Sends a request whose success body deserializes as <typeparamref name="T"/>.</summary>
    internal static async Task<Result<T, OcctooError>> Send<T>(
        HttpClient httpClient,
        TimeSpan timeout,
        HttpRequestMessage request,
        string operation,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using var activity = OcctooTelemetry.Source.StartActivity(operation, ActivityKind.Client);

            var outcome = await Send(httpClient, timeout, request, cancellationToken)
                .Bind(async Task<Result<T, OcctooError>> (response) =>
                {
                    using (response)
                    {
                        return response.IsSuccessStatusCode
                            ? await Read(response, operation, responseType, cancellationToken).ConfigureAwait(false)
                            : await OcctooApiErrors.Classify(response, Describe(operation), cancellationToken).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            return outcome.TapError(error => OcctooTelemetry.Fail(activity, error));
        }
    }

    /// <summary>Sends a request whose success carries no body (202/204).</summary>
    internal static async Task<UnitResult<OcctooError>> Send(
        HttpClient httpClient,
        TimeSpan timeout,
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using var activity = OcctooTelemetry.Source.StartActivity(operation, ActivityKind.Client);

            var outcome = await Send(httpClient, timeout, request, cancellationToken)
                .Bind(async Task<UnitResult<OcctooError>> (response) =>
                {
                    using (response)
                    {
                        return response.IsSuccessStatusCode
                            ? UnitResult.Success<OcctooError>()
                            : await OcctooApiErrors.Classify(response, Describe(operation), cancellationToken).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

            return outcome.TapError(error => OcctooTelemetry.Fail(activity, error));
        }
    }

    /// <summary>
    /// Maps a parsed response onto the public model. A body that parsed but
    /// lacks required values fails the model's own validation; that is an
    /// <see cref="UnexpectedError"/>, not an exception.
    /// </summary>
    internal static async Task<Result<TModel, OcctooError>> MapResponse<T, TModel>(
        this Task<Result<T, OcctooError>> sent,
        Func<T, TModel> map)
    {
        var result = await sent.ConfigureAwait(false);
        if (result.IsFailure)
            return result.Error;

        try
        {
            return map(result.Value);
        }
        catch (Exception exception) when (exception is Vogen.ValueObjectValidationException or MalformedResponseException)
        {
            return new UnexpectedError($"Occtoo returned an incomplete response: {exception.Message}");
        }
    }

    /// <summary>
    /// The elements of a response array, refusing a null element — JSON
    /// nullability annotations cover properties, not the items of a collection.
    /// </summary>
    internal static IReadOnlyList<T> Elements<T>(IReadOnlyList<T> items, string field) =>
        items.Any(item => item is null)
            ? throw new MalformedResponseException($"'{field}' contains a null element.")
            : items;

    internal static HttpRequestMessage Request(HttpMethod method, Uri uri) => new(method, uri);

    internal static HttpRequestMessage Request<TBody>(HttpMethod method, Uri uri, TBody body, JsonTypeInfo<TBody> bodyType) =>
        new(method, uri) { Content = JsonContent.Create(body, bodyType) };

    private static async Task<Result<T, OcctooError>> Read<T>(
        HttpResponseMessage response,
        string operation,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync(responseType, cancellationToken).ConfigureAwait(false);
            return value is not null
                ? value
                : new UnexpectedError($"{Describe(operation)} returned an empty body.", (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new UnexpectedError(
                $"{Describe(operation)} returned a body that could not be parsed: {exception.Message}",
                (int)response.StatusCode);
        }
    }

    // Span names are lowercase verbs ("list sources"); messages want a sentence start.
    private static string Describe(string operation) =>
        char.ToUpperInvariant(operation[0]) + operation[1..];
}

/// <summary>A parsed response that breaks the API's contract; mapped to an <see cref="UnexpectedError"/>.</summary>
internal sealed class MalformedResponseException(string message) : Exception(message);
