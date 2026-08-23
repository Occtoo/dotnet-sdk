// A worker service that consumes Occtoo events in paginated fashion.
//
// Shows the three pieces an event consumer wires together:
//
//   1. configuration  — the "Occtoo" section of appsettings.json (in a real
//                       deployment, override the secret via the environment:
//                       Occtoo__ClientSecret)
//   2. DI             — AddOcctooClient over IHttpClientFactory, one shared
//                       client and credential for the whole host
//   3. consumption    — a BackgroundService pulling pages of typed events,
//                       persisting the `after` cursor after each processed
//                       page so a restart resumes without loss or replay
//
// Fill in the placeholders in appsettings.json and `dotnet run`. Stop it,
// ingest more entries, start it again — it picks up from checkpoint.json.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Occtoo;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.Sdk.Examples.Events.Pull;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configuration: bind the "Occtoo" section and fail fast when incomplete.
var settings = builder.Configuration.GetSection("Occtoo").Get<OcctooSettings>() ?? new OcctooSettings();
settings.Validate();
builder.Services.AddSingleton(settings);

// 2. DI: a singleton OcctooClient behind IHttpClientFactory, authentication in
//    the handler pipeline.
builder.Services.AddOcctooClient(options => options with
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(settings.ClientId),
            Audience = Audience.From(settings.TenantId),
            Scopes = [OcctooScopes.ReadEventsPull],
        },
        ClientSecret.From(settings.ClientSecret)),
});

// 3. The consumption loop itself.
builder.Services.AddHostedService<EventConsumerWorker>();

await builder.Build().RunAsync();
