using System.Net;

namespace Occtoo.Sdk.Tests;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/>. Every test in this project goes
/// through one of these — nothing here reaches a real Occtoo environment.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly Lock _gate = new();

    /// <summary>Every request the handler was asked to send, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    public int RequestCount => Requests.Count;

    public StubHandler Respond(HttpStatusCode statusCode, string json)
    {
        _responses.Enqueue((_, _) => Task.FromResult(JsonResponse(statusCode, json)));
        return this;
    }

    public StubHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue((request, _) => Task.FromResult(respond(request)));
        return this;
    }

    /// <summary>
    /// Answers asynchronously, so a test can hold a request open
    /// </summary>
    public StubHandler Respond(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    /// <summary>Replies with this response to every request from now on.</summary>
    public StubHandler RespondAlways(HttpStatusCode statusCode, string json)
    {
        Always = (_, _) => Task.FromResult(JsonResponse(statusCode, json));
        return this;
    }

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Always { get; set; }

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

        IReadOnlyDictionary<string, string> contentHeaders = request.Content is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : request.Content.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);

        // Asset uploads run several requests at once, so recording and picking
        // the answer are serialized here
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

        lock (_gate)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                headers,
                body,
                contentHeaders));

            respond = _responses.Count > 0
                ? _responses.Dequeue()
                : Always ?? throw new InvalidOperationException(
                    $"Unexpected request to {request.RequestUri}: the stub has no response left to give.");
        }

        return await respond(request, cancellationToken);
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
    string? Body,
    IReadOnlyDictionary<string, string> ContentHeaders)
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

    /// <summary>A header that belongs to the body — Content-Length, Content-Type.</summary>
    public string? ContentHeader(string name) => ContentHeaders.GetValueOrDefault(name);
}
