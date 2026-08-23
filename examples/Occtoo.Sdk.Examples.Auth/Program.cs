// Every way to authenticate against Occtoo. Each flow is its own file with
// its own placeholders:
//
//   dotnet run -- clientcredentials     ClientCredentialsExample.cs (default)
//   dotnet run -- apikey                ApiKeyExample.cs
//   dotnet run -- devicelogin           DeviceLoginExample.cs
//
// Authenticate() establishes the credential without calling a business API,
// so a bad secret fails here rather than inside the first real request. What
// to do with the authenticated client next is the other examples:
// Sources.Ingest, Events.Pull, Events.SSE.

using CSharpFunctionalExtensions;
using Occtoo;
using Occtoo.Sdk.Examples.Auth;

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

// For device login this is where the browser prompt happens. The token is
// cached and refreshed by the SDK from here on — no call ever fetches one
// per request.
return await client
    .Authenticate()
    .Tap(token => Console.WriteLine(token.HasValue
        ? $"Authenticated. Token expires {token.Value.ExpiresOn:u}."
        : "Credential applied. An API key involves no token exchange, so it is proven on the first real call."))
    .TapError(error => Console.Error.WriteLine(error switch
    {
        // The error hierarchy is what you branch on: TransientError means
        // retry, everything else means something has to change first.
        TransientError transient => $"Temporary failure, retry with backoff: {transient.Message}",
        _ => $"Credential rejected: {error}",
    }))
    .Finally(result => result.IsSuccess ? 0 : 1);
