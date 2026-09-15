using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CSharpFunctionalExtensions;
using Occtoo.Telemetry;

namespace Occtoo.Http.Internal;

/// <summary>
/// The request/response shape shared by the resource-management surfaces:
/// one span per operation, transport failures and API rejections as results,
/// source-generated JSON in both directions.
/// </summary>
internal sealed class OcctooJsonApi(HttpClient httpClient, TimeSpan requestTimeout)
{
    /// <summary>Sends a request whose success body deserializes as <typeparamref name="T"/>.</summary>
    internal async Task<Result<T, OcctooError>> Send<T>(
        HttpRequestMessage request,
        string operation,
        JsonTypeInfo<T> responseType,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using var activity = OcctooTelemetry.Source.StartActivity(operation, ActivityKind.Client);

            var outcome = await OcctooTransport
                .Send(httpClient, request, requestTimeout, cancellationToken)
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
    internal async Task<UnitResult<OcctooError>> Send(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using var activity = OcctooTelemetry.Source.StartActivity(operation, ActivityKind.Client);

            var outcome = await OcctooTransport
                .Send(httpClient, request, requestTimeout, cancellationToken)
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
