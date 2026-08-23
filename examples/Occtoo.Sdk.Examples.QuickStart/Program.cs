// Quick start. Pick how to authenticate; everything after that is the same
// two steps regardless of the flow:
//
//   step 1  authenticate  — prove the credential works before any real call
//   step 2  ingest        — upsert one typed entry into a source
//
// Each flow is its own file with its own placeholders:
//
//   dotnet run -- clientcredentials     ClientCredentialsExample.cs (default)
//   dotnet run -- apikey                ApiKeyExample.cs
//   dotnet run -- devicelogin           DeviceLoginExample.cs

using CSharpFunctionalExtensions;
using Occtoo;
using Occtoo.Sdk.Examples.QuickStart;
using Occtoo.Sources;

// ── Shared configuration ───────────────────────────────────────────────────
const string OcctooSourceId = "sdk";
// ───────────────────────────────────────────────────────────────────────────

var credential = (args.FirstOrDefault() ?? "clientcredentials") switch
{
    "clientcredentials" => ClientCredentialsExample.Create(),
    "apikey" => ApiKeyExample.Create(),
    "devicelogin" => DeviceLoginExample.Create(),
    _ => null,
};

if (credential is null)
{
    Console.Error.WriteLine("Usage: dotnet run -- [clientcredentials|apikey|devicelogin]");
    return 2;
}

using var client = new OcctooClient(new OcctooClientOptions
{
    Credential = credential,
});

Console.WriteLine($"Occtoo API: {client.BaseAddress}");

// The values keep their native JSON types; properties the source does not know
// yet get their type inferred from the value.
var entry = SourceEntry
    .WithId("quickstart-1")
    .WithLocalizedText("name", "Blue chair", "en")
    .WithDecimal("price", 100.111m)
    .WithBoolean("inStock", true)
    .WithTimestamp("publishedAt", DateTimeOffset.UtcNow)
    .WithList("tags", "quickstart", "sample");

return await client

    // ── Step 1: authenticate ───────────────────────────────────────────────
    // Establishes the credential without calling an API, so a bad secret fails
    // here rather than inside the first import. For device login this is where
    // the browser prompt happens.
    .Authenticate()
    .Tap(token => Console.WriteLine(token.HasValue
        ? $"Authenticated. Token expires {token.Value.ExpiresOn:u}."
        : "Credential applied. An API key involves no token exchange."))

    // ── Step 2: ingest ─────────────────────────────────────────────────────
    // Requires the credential to carry the write:sources scope — an API key or
    // a token without it demonstrates the typed error surface instead.
    .Bind(_ => client.Sources.IngestEntries(SourceId.From(OcctooSourceId), [entry]))
    .Tap(PrintReceipt)
    .TapError(PrintError)
    .Finally(result => result.IsSuccess ? 0 : 1);

static void PrintReceipt(IngestReceipt receipt)
{
    Console.WriteLine(
        $"Accepted {receipt.AcceptedEntryCount} entries into '{receipt.SourceId.Value}' " +
        $"at {receipt.AcceptedAt:u} (correlation {receipt.CorrelationId.Value}).");

    foreach (var found in receipt.NewProperties)
        Console.WriteLine($"  new property inferred: {found.Id.Value} ({found.Type})");
}

// The error hierarchy is what you branch on: TransientError means retry,
// everything else means something has to change first.
static void PrintError(OcctooError error) =>
    Console.Error.WriteLine(error switch
    {
        RateLimitError { RetryAfter.HasValue: true } limited =>
            $"Throttled — retry after {limited.RetryAfter.Value}.",
        TransientError transient => $"Temporary failure, retry with backoff: {transient.Message}",
        ValidationError invalid =>
            $"Rejected: {invalid.Message}\n  " +
            string.Join("\n  ", invalid.Failures.Select(pair => $"{pair.Key}: {string.Join("; ", pair.Value)}")),
        _ => $"Failed: {error}",
    });
