# Events: pull and stream

`client.Events` wraps the Events API — every change in the tenant, delivered as
[CloudEvents](https://cloudevents.io/): pull retained events page by page
(`GET /v1/events`), or subscribe to the live Server-Sent Events stream
(`GET /v1/events/stream`).

```csharp
var page = await client.Events.Pull(new EventQuery
{
    Filter = Maybe.From(EventFilter.OfType<SourceEntryEvent>(e => e.WithSource("products"))),
    Limit = 200,
});

page.Tap(p =>
{
    foreach (var evt in p.Items)
        Handle(evt);
    checkpoint.Save(p.Next); // persist only after the page is processed
});
```

Requires a credential with an events scope — see
[authentication.md](authentication.md).

## The model: one record per event type

Every documented event type is a sealed record — 30 of them, grouped under nine
abstract family bases — and the concrete type *is* the event type. Consuming
events is pattern matching:

```csharp
switch (evt)
{
    case SourceEntryAdded e:   Sync(e.SourceId, e.EntryKey);  break;
    case SourceEntryDeleted e: Remove(e.EntryKey);            break;
    case SourceEntryEvent e:   Log(e.SourceId);               break; // any other source_entry.*
    case UnknownEvent e:       LogUnknown(e.Type, e.Data);    break;
}
```

An event type this SDK version does not know arrives as `UnknownEvent` — the
envelope fully populated, the payload raw — never silently dropped, so a new
platform event type does not break an old consumer.

The envelope is shared by every record (`Id`, `Type`, `Sequence`, `Source`,
`Subject`, `Time`, `CorrelationIds`, `Actor`); the payload is each record's
primary constructor. Absent optional fields are `Maybe<T>.None`, never null.

## Filters: invalid combinations do not compile

`EventFilter` builds the API's filter grammar fluently, anchored on event
types. Each type only offers the conditions the API accepts for it — the
condition methods are gated on capability markers (`IFilterableBySource` and
friends), so filtering `card_definition.updated` by `sourceId` is a compile
error (CS0311), not a runtime `400`:

```csharp
var filter = EventFilter
    .OfType<SourceEntryAdded>(e => e.WithSource("products", "assets"))
    .OrType<SegmentUpdated>(e => e.WithSegment("summer-sale"));

// (type eq "source_entry.added" and (sourceId eq "products" or sourceId eq "assets"))
//   or (type eq "segment.updated" and segmentId eq "summer-sale")
```

A family base expands to all its members — `OfType<SourceEntryEvent>()` matches
every `source_entry.*` type. For grammar the builder does not model,
`EventFilter.Raw("...")` sends an expression verbatim.

## Cursors and checkpointing

A pull page returns `Page<CloudEvent>`: `Items`, `HasMore`, an optional
`Total` (opt in with `EventQuery.IncludeTotal`), and `Next` — an opaque
`EventCursor` that continues the read strictly after the page. Every event also
carries its `Sequence`, and `sequence.AsCursor()` rebuilds a cursor from a
processed event when a stored cursor was lost.

Two rules keep checkpointing correct:

- **Persist a cursor only after its page is processed.** The cursor points past
  the page; saving it earlier loses events on a crash.
- **Persist the filter alongside the cursor.** A cursor is a position in the
  tenant stream, not in a filtered view — resuming the same position with a
  broader filter silently skips events that the old filter excluded.

`PullAll` folds the pagination loop into one `await foreach`, fetching pages
lazily until the retained stream is exhausted — the catch-up path.

### Metadata: the stream's shape without its payloads

`GetMetadata` (`GET /v1/events/metadata`) reports the retained stream — or one
filtered view of it — as positions and a count: `First`, `Latest` (each a
sequence with an optional time), `After` (the pull cursor for the tail), and
the exact `Total`. Three situations call for it:

```csharp
var metadata = await client.Events.GetMetadata(filter);

metadata.Tap(m =>
{
    // Start consuming from "now", skipping history:
    var live = client.Events.Stream(new EventStreamOptions { Filter = filter, After = m.After });

    // Measure consumer lag: my checkpoint vs. m.Latest.

    // Detect an expired checkpoint: a stored cursor before m.First means the
    // gap was dropped from retention — decide whether to resync or accept it.
});
```

All values are absent (and `Total` is `0`) when nothing retained matches the
filter.

A runnable checkpointing consumer lives at
[`examples/Occtoo.Sdk.Examples.Events.Pull`](../examples/Occtoo.Sdk.Examples.Events.Pull):
a worker that drains new events page by page, persists the cursor (with its
filter) after each processed page, and resumes exactly there after a restart.

## Streaming

`Stream` subscribes to the live SSE stream and yields events as they occur:

```csharp
await foreach (var evt in client.Events.Stream(new EventStreamOptions
{
    Filter = Maybe.From(EventFilter.OfType<SourceEntryEvent>()),
    After = checkpoint.Load(),
}, stoppingToken))
{
    Handle(evt);
}
```

Dropped connections are re-established automatically with jittered exponential
backoff (`InitialReconnectDelay` doubling to `MaxReconnectDelay`, reset once
connected), resuming via the SSE `Last-Event-ID` convention exactly after the
last delivered event — no events lost, none replayed. Heartbeats are consumed
internally. With no `After`, the stream starts at the current tail; combine
`PullAll` (catch up) with `Stream` (follow) for a full replay-then-follow
consumer.

The SSE parsing is the in-box `System.Net.ServerSentEvents` — no extra
dependency, trim- and AOT-safe.

## The exception to the no-throw rule

`Pull` returns `Result<Page<CloudEvent>, OcctooError>` like the rest of the
SDK. The two enumerables — `PullAll` and `Stream` — cannot: an
`IAsyncEnumerable` has no failure track. They retry and reconnect through
transient failures internally, and throw `OcctooEventsException` (carrying the
typed `OcctooError`) only for failures no retry can fix — a revoked credential,
an invalid filter. Caller cancellation surfaces as
`OperationCanceledException`, as everywhere.

Long-lived streams are why the SDK's `HttpClient` runs with an infinite
client-level timeout: `OcctooClientOptions.Timeout` is enforced per request —
and, for streams, bounds only connection establishment.
