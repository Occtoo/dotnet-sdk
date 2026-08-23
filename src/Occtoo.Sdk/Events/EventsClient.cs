using System.Diagnostics;
using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Http.Internal;
using Occtoo.Logging;
using Occtoo.Telemetry;

namespace Occtoo.Events;

/// <summary>
/// The Events feature — consuming tenant-scoped CloudEvents by pull or live
/// stream. Reached through <see cref="OcctooClient.Events"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Pull"/> pages through retained events and returns results like
/// the rest of the SDK. The two enumerables — <see cref="PullAll"/> and
/// <see cref="Stream"/> — reconnect and retry transient failures internally
/// and throw <see cref="OcctooEventsException"/> only for failures no retry
/// can fix; an <c>IAsyncEnumerable</c> has no failure track, which makes this
/// the SDK's one deliberate throwing surface.
/// </para>
/// <para>
/// Requires a credential with an events scope — see
/// <see cref="Authentication.OcctooScopes"/>.
/// </para>
/// </remarks>
public sealed class EventsClient
{
    private const int MaxLimit = 1000;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal EventsClient(HttpClient httpClient, ILogger logger, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    /// <summary>
    /// Reads one page of retained events in ascending sequence order.
    /// </summary>
    /// <param name="query">What to read; defaults to everything from the start.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The page, whose <see cref="Page{T}.Next"/> cursor continues the read —
    /// persist it only after the page has been processed, together with the
    /// filter it was earned under.
    /// </returns>
    public async Task<Result<Page<CloudEvent>, OcctooError>> Pull(
        EventQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new EventQuery();

        if (query.Limit is < 1 or > MaxLimit)
            return new ValidationError($"Limit must be between 1 and {MaxLimit}.");

        using var activity = OcctooTelemetry.Source.StartActivity("pull events", ActivityKind.Client);
        activity?.SetTag("occtoo.events.limit", query.Limit);

        using var request = new HttpRequestMessage(HttpMethod.Get, PullUri(query));

        var outcome = await OcctooTransport
            .Send(_httpClient, request, _requestTimeout, cancellationToken)
            .Bind(async Task<Result<Page<CloudEvent>, OcctooError>> (response) =>
            {
                using (response)
                {
                    return response.StatusCode == HttpStatusCode.OK
                        ? await ReadPage(response, cancellationToken).ConfigureAwait(false)
                        : await OcctooApiErrors
                            .Classify(response, "Pulling events", cancellationToken)
                            .ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

        return outcome
            .Tap(page =>
            {
                activity?.SetTag("occtoo.events.count", page.Items.Count);
                OcctooLog.EventsPulled(_logger, page.Items.Count, page.HasMore);
            })
            .TapError(error => OcctooTelemetry.Fail(activity, error));
    }

    /// <summary>
    /// Reads retained events from the query's position until the stream is
    /// exhausted, fetching pages lazily — the catch-up loop as one
    /// <c>await foreach</c>.
    /// </summary>
    /// <param name="query">What to read; <see cref="EventQuery.Limit"/> sizes the underlying pages.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    /// <returns>The events, in stream order.</returns>
    /// <exception cref="OcctooEventsException">A page failed for a reason retrying cannot fix.</exception>
    public async IAsyncEnumerable<CloudEvent> PullAll(
        EventQuery? query = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        query ??= new EventQuery();

        while (true)
        {
            var page = await Pull(query, cancellationToken).ConfigureAwait(false);
            if (page.IsFailure)
                throw new OcctooEventsException(page.Error);

            foreach (var evt in page.Value.Items)
                yield return evt;

            if (!page.Value.HasMore || page.Value.Next.HasNoValue)
                yield break;

            query = query with { After = page.Value.Next };
        }
    }

    /// <summary>
    /// Subscribes to the live event stream. The connection is re-established
    /// automatically with exponential backoff, resuming exactly after the last
    /// delivered event — no events lost, none replayed.
    /// </summary>
    /// <param name="options">What to subscribe to; defaults to everything from the current tail.</param>
    /// <param name="cancellationToken">Ends the subscription.</param>
    /// <returns>The events, as they occur.</returns>
    /// <exception cref="OcctooEventsException">The stream failed for a reason reconnecting cannot fix.</exception>
    public async IAsyncEnumerable<CloudEvent> Stream(
        EventStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new EventStreamOptions();
        options.Validate();

        var lastDelivered = options.After.Map(cursor => cursor.Value);
        var delay = options.InitialReconnectDelay;
        var firstConnect = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!firstConnect)
            {
                OcctooLog.EventStreamReconnecting(_logger, delay);
                await Task.Delay(Jittered(delay), cancellationToken).ConfigureAwait(false);
                delay = Min(TimeSpan.FromTicks(delay.Ticks * 2), options.MaxReconnectDelay);
            }

            firstConnect = false;

            using var activity = OcctooTelemetry.Source.StartActivity("stream events", ActivityKind.Client);
            var connected = await Connect(options, lastDelivered, cancellationToken).ConfigureAwait(false);

            if (connected.IsFailure)
            {
                OcctooTelemetry.Fail(activity, connected.Error);
                if (connected.Error is TransientError)
                    continue;

                throw new OcctooEventsException(connected.Error);
            }

            using var response = connected.Value;
            OcctooLog.EventStreamConnected(_logger);
            delay = options.InitialReconnectDelay;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var frames = SseParser.Create(stream).EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using (frames.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await frames.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is HttpRequestException or IOException)
                    {
                        // The connection dropped mid-stream; reconnect resumes
                        // strictly after the last delivered event.
                        break;
                    }

                    if (!moved)
                        break; // Server closed the stream; reconnect.

                    var frame = frames.Current;
                    var parsed = ParseFrame(frame);
                    if (parsed.HasNoValue)
                        continue;

                    yield return parsed.Value;

                    lastDelivered = frame.EventId is { Length: > 0 } id
                        ? Maybe.From(id)
                        : Maybe.From(parsed.Value.Sequence.Value);
                }
            }
        }
    }

    private Maybe<CloudEvent> ParseFrame(SseItem<string> frame)
    {
        if (string.IsNullOrWhiteSpace(frame.Data))
            return Maybe<CloudEvent>.None;

        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            var parsed = Internal.EventParser.Parse(document.RootElement);
            if (parsed.IsFailure)
            {
                OcctooLog.EventSkipped(_logger, parsed.Error.Message);
                return Maybe<CloudEvent>.None;
            }

            return parsed.Value;
        }
        catch (JsonException exception)
        {
            OcctooLog.EventSkipped(_logger, exception.Message);
            return Maybe<CloudEvent>.None;
        }
    }

    private async Task<Result<HttpResponseMessage, OcctooError>> Connect(
        EventStreamOptions options,
        Maybe<string> lastDelivered,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, StreamUri(options));
        request.Headers.Accept.ParseAdd("text/event-stream");

        // Resuming mid-subscription uses the SSE convention; the server gives
        // Last-Event-ID precedence over the query parameter.
        if (lastDelivered.HasValue)
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastDelivered.Value);

        // The per-request timeout bounds only connection establishment: the
        // stream itself is long-lived by design.
        var sent = await OcctooTransport
            .Send(_httpClient, request, _requestTimeout, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        request.Dispose();

        return await sent.Bind(async Task<Result<HttpResponseMessage, OcctooError>> (response) =>
        {
            if (response.StatusCode == HttpStatusCode.OK)
                return response;

            using (response)
            {
                return await OcctooApiErrors
                    .Classify(response, "Streaming events", cancellationToken)
                    .ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private static Uri PullUri(EventQuery query)
    {
        var builder = new StringBuilder("v1/events?limit=").Append(query.Limit);

        if (query.IncludeTotal)
            builder.Append("&total=true");
        if (query.After.HasValue)
            builder.Append("&after=").Append(Uri.EscapeDataString(query.After.Value.Value));
        if (query.Filter.HasValue)
            builder.Append("&filter=").Append(Uri.EscapeDataString(query.Filter.Value.ToString()));

        return new Uri(builder.ToString(), UriKind.Relative);
    }

    private static Uri StreamUri(EventStreamOptions options)
    {
        var builder = new StringBuilder("v1/events/stream");
        var separator = '?';

        if (options.After.HasValue)
        {
            builder.Append(separator).Append("after=").Append(Uri.EscapeDataString(options.After.Value.Value));
            separator = '&';
        }

        if (options.Filter.HasValue)
            builder.Append(separator).Append("filter=").Append(Uri.EscapeDataString(options.Filter.Value.ToString()));

        return new Uri(builder.ToString(), UriKind.Relative);
    }

    private async Task<Result<Page<CloudEvent>, OcctooError>> ReadPage(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var items = new List<CloudEvent>();
            if (root.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var envelope in array.EnumerateArray())
                {
                    var parsed = Internal.EventParser.Parse(envelope);
                    if (parsed.IsSuccess)
                        items.Add(parsed.Value);
                    else
                        OcctooLog.EventSkipped(_logger, parsed.Error.Message);
                }
            }

            var next = root.TryGetProperty("after", out var after)
                       && after.ValueKind == JsonValueKind.String
                       && after.GetString() is { Length: > 0 } cursor
                ? Maybe.From(EventCursor.From(cursor))
                : Maybe<EventCursor>.None;

            var total = root.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number
                ? Maybe.From(count.GetInt64())
                : Maybe<long>.None;

            var hasMore = root.TryGetProperty("hasMore", out var more) && more.ValueKind == JsonValueKind.True;

            return new Page<CloudEvent>(items, next, hasMore, total);
        }
        catch (JsonException exception)
        {
            return new UnexpectedError($"The events page could not be parsed: {exception.Message}");
        }
    }

    private static TimeSpan Jittered(TimeSpan delay) =>
        TimeSpan.FromTicks((long)(delay.Ticks * (0.8 + (Random.Shared.NextDouble() * 0.4))));

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
