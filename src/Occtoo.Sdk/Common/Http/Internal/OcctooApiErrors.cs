using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>
/// Turns a non-success Occtoo API response into the matching
/// <see cref="OcctooError"/>, so every surface classifies failures the same way.
/// </summary>
internal static class OcctooApiErrors
{
    internal static async Task<OcctooError> Classify(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var problem = await ReadProblem(response, cancellationToken).ConfigureAwait(false);
        var detail = Describe(problem, response.StatusCode);

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ValidationError(
                $"{operation} was rejected: {detail}",
                Failures(problem)),
            HttpStatusCode.Unauthorized => new AuthenticationError(
                $"{operation} was refused: authentication is missing or invalid. {detail}"),
            HttpStatusCode.Forbidden => new ForbiddenError(
                $"{operation} was refused: the credential lacks the required scope or resource grant. {detail}"),
            HttpStatusCode.NotFound => new NotFoundError(
                $"{operation} failed: {detail}"),
            HttpStatusCode.Conflict => new ConflictError(
                $"{operation} was rejected: {detail}"),
            HttpStatusCode.TooManyRequests => new RateLimitError(
                $"{operation} was throttled: the tenant request-rate budget is exhausted.",
                RetryAfter(response)),
            >= HttpStatusCode.InternalServerError => new ServerError(
                $"{operation} failed on Occtoo's side: {detail}",
                (int)response.StatusCode),
            _ => new UnexpectedError(
                $"{operation} received an unexpected response: {detail}",
                (int)response.StatusCode),
        };
    }

    internal static Maybe<TimeSpan> RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow is var wait && wait > TimeSpan.Zero
                ? wait
                : Maybe<TimeSpan>.None,
            _ => Maybe<TimeSpan>.None,
        };

    private static string Describe(ProblemDetailsDto? problem, HttpStatusCode status)
    {
        var text = problem switch
        {
            { Detail.Length: > 0 } => problem.Detail,
            { Title.Length: > 0 } => problem.Title,
            _ => $"Occtoo responded {(int)status}.",
        };

        // The traceId is what Occtoo support needs to trace the call.
        return problem?.TraceId is { Length: > 0 } traceId
            ? $"{text} (traceId: {traceId})"
            : text;
    }

    private static Dictionary<string, IReadOnlyList<string>> Failures(ProblemDetailsDto? problem) =>
        problem?.Errors is { Count: > 0 } errors
            ? errors.ToDictionary(
                pair => pair.Key,
                IReadOnlyList<string> (pair) => pair.Value)
            : new Dictionary<string, IReadOnlyList<string>>();

    private static async Task<ProblemDetailsDto?> ReadProblem(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync(ProblemJsonContext.Default.ProblemDetailsDto, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A non-JSON body (a gateway error page, say) — the status alone
            // still classifies the failure.
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// RFC 9457 problem details, covering both the plain and the validation shape
/// Occtoo returns. Naming comes from the context's Web defaults (camelCase).
/// </summary>
internal sealed record ProblemDetailsDto
{
    public string? Title { get; init; }

    public string? Detail { get; init; }

    public int Status { get; init; }

    public string? TraceId { get; init; }

    public Dictionary<string, string[]>? Errors { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProblemDetailsDto))]
internal sealed partial class ProblemJsonContext : JsonSerializerContext;
