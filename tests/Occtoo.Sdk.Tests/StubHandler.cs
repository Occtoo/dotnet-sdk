using System.Net;

namespace Occtoo.Sdk.Tests;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/>. Every test in this project goes
/// through one of these — nothing here reaches a real Occtoo environment.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    /// <summary>Every request the handler was asked to send, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    public int RequestCount => Requests.Count;

    public StubHandler Respond(HttpStatusCode statusCode, string json)
    {
        _responses.Enqueue(_ => JsonResponse(statusCode, json));
        return this;
    }

    public StubHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    /// <summary>Replies with this response to every request from now on.</summary>
    public StubHandler RespondAlways(HttpStatusCode statusCode, string json)
    {
        Always = _ => JsonResponse(statusCode, json);
        return this;
    }

    private Func<HttpRequestMessage, HttpResponseMessage>? Always { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        // Snapshot the headers: the SDK reuses the same HttpRequestMessage when it
        // retries, so holding a reference would show later mutations here.
        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            headers,
            body));

        if (_responses.Count > 0)
            return _responses.Dequeue()(request);

        if (Always is { } always)
            return always(request);

        throw new InvalidOperationException(
            $"Unexpected request to {request.RequestUri}: the stub has no response left to give.");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body)
{
    /// <summary>Parses a form-encoded body into its parameters.</summary>
    public IReadOnlyDictionary<string, string> Form =>
        Body is null or ""
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Body
                .Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    pair => Uri.UnescapeDataString(pair[0]),
                    pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "",
                    StringComparer.Ordinal);

    public string? Header(string name) => Headers.GetValueOrDefault(name);
}
