// Live event consumption over Server-Sent Events: subscribe once, receive
// events as they occur, let the SDK reconnect and resume for you.
//
// Fill in the placeholders and `dotnet run`. Then change data in Occtoo (or
// run the Sources.Ingest example) and watch the events arrive. Ctrl+C stops.

using Occtoo;
using Occtoo.Authentication;
using Occtoo.Events;

// ── Configuration ──────────────────────────────────────────────────────────
const string OcctooClientId = "<your-application-client-id>";
const string OcctooClientSecret = "<your-application-client-secret>";
const string OcctooTenantId = "<your-tenant-id>";
const string OcctooSourceId = "<your-source-id>";
// ───────────────────────────────────────────────────────────────────────────

using var client = new OcctooClient(new OcctooClientOptions
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(OcctooClientId),
            Audience = Audience.From(OcctooTenantId),
            Scopes = [OcctooScopes.ReadEventsSse],
        },
        ClientSecret.From(OcctooClientSecret)),
});

// Ctrl+C cancels the enumeration; the SDK closes the connection cleanly.
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stopping.Cancel();
};

// The filter narrows the stream server-side, and only conditions valid for
// the chosen event types compile. Without `After` the subscription starts at
// the current tail: only events that occur from now on are delivered. To
// resume from a checkpoint instead, pass the stored cursor as `After` — see
// the Events.Pull example for earning and persisting one.
var options = new EventStreamOptions
{
    Filter = EventFilter
        .OfType<SourceEntryEvent>(e => e.WithSource(OcctooSourceId))
        .OrType<SourceEvent>(e => e.WithSource(OcctooSourceId)),
};

Console.WriteLine($"Streaming events from {client.BaseAddress} — Ctrl+C to stop.");

try
{
    // Dropped connections reconnect automatically with backoff, resuming
    // exactly after the last delivered event — no events lost, none replayed.
    // The loop body only ever sees events; heartbeats are consumed internally.
    await foreach (var evt in client.Events.Stream(options, stopping.Token))
    {
        Console.WriteLine(evt switch
        {
            SourceEntryAdded e => $"[{e.Sequence.Value}] entry '{e.EntryKey.Value}' added to '{e.SourceId.Value}'",
            SourceEntryUpdated e => $"[{e.Sequence.Value}] entry '{e.EntryKey.Value}' updated ({e.ChangedProperties.Count} properties)",
            SourceEntryDeleted e => $"[{e.Sequence.Value}] entry '{e.EntryKey.Value}' deleted from '{e.SourceId.Value}'",
            UnknownEvent e => $"[{e.Sequence.Value}] unknown event type '{e.Type}'",
            _ => $"[{evt.Sequence.Value}] {evt.Type}",
        });
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — the normal way out of an endless subscription.
    Console.WriteLine("Stopped.");
}
catch (OcctooEventsException exception)
{
    // Transient failures never reach here — the SDK reconnects through them.
    // This is a revoked credential, a missing scope, an invalid filter:
    // something a human has to fix, typed on the exception's Error.
    Console.Error.WriteLine($"Stream cannot continue: {exception.Error}");
    return 1;
}

return 0;
