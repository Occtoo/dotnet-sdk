// A worker service that consumes Occtoo events from a durable consumer: leased
// batches, pulled and acknowledged by competing workers, with the position
// tracked by Occtoo — no cursor or checkpoint file to keep.
//
// Shows the three pieces a durable-consumer worker wires together:
//
//   1. configuration  — the "Occtoo" section of appsettings.json (in a real
//                       deployment, override the secret via the environment:
//                       Occtoo__ClientSecret)
//   2. DI             — AddOcctooClient over IHttpClientFactory, one shared
//                       client and credential for the whole host
//   3. consumption    — a BackgroundService running several pull → process →
//                       acknowledge loops, each with its own stable worker id
//
// Create a durable consumer event destination in Occtoo, fill in the
// placeholders in appsettings.json and `dotnet run`. Start a second copy and
// the two processes share the batches; stop one mid-batch and its lease is
// redelivered to the other.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Occtoo;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.Sdk.Examples.Events.Batch;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configuration: bind the "Occtoo" section and fail fast when incomplete.
var settings = builder.Configuration.GetSection("Occtoo").Get<OcctooSettings>() ?? new OcctooSettings();
settings.Validate();
builder.Services.AddSingleton(settings);

// 2. DI: a singleton OcctooClient behind IHttpClientFactory, authentication in
//    the handler pipeline. Durable consumers need the umbrella read:events
//    scope — the pull- and SSE-only scopes do not cover them.
builder.Services.AddOcctooClient(options => options with
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(settings.ClientId),
            Audience = Audience.From(settings.TenantId),
            Scopes = [OcctooScopes.ReadEvents],
        },
        ClientSecret.From(settings.ClientSecret)),
});

// 3. The consumption loops themselves.
builder.Services.AddHostedService<DurableConsumerWorker>();

await builder.Build().RunAsync();
