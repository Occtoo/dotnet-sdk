// A worker service that turns file events from the system that owns the files
// (a PIM, a DAM) into an Occtoo Media data source, one call per event.
//
// Shows the three pieces a real service wires together:
//
//   1. configuration  — the "Occtoo" section of appsettings.json
//   2. DI             — AddOcctooClient over IHttpClientFactory, one shared
//                       client and credential for the whole host
//   3. sync           — a BackgroundService that uploads added files, replaces
//                       updated ones, ingests changed metadata and deletes
//                       removed ones
//
// Fill in the placeholders in appsettings.json, pass the secret as
// Occtoo__ClientSecret or --Occtoo:ClientSecret, and `dotnet run`.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Occtoo;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.Sdk.Examples.Assets.Sync;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configuration: bind the "Occtoo" section and fail fast when incomplete.
var settings = builder.Configuration.GetSection("Occtoo").Get<OcctooSettings>() ?? new OcctooSettings();
settings.Validate();
builder.Services.AddSingleton(settings);

// 2. DI: a singleton OcctooClient behind IHttpClientFactory, authentication in
//    the handler pipeline. The builder it returns is a normal
//    IHttpClientBuilder — resilience or logging handlers layer on here.
builder.Services.AddOcctooClient(options => options with
{
    Credential = OcctooCredential.ClientCredentials(
        new OcctooAuthorityOptions
        {
            ClientId = ClientId.From(settings.ClientId),
            Audience = Audience.From(settings.TenantId),

            // Uploading and ingesting need write:sources; reading the data
            // source, its properties and its entries needs read:sources.
            Scopes = [OcctooScopes.WriteSources, OcctooScopes.ReadSources],
        },
        ClientSecret.From(settings.ClientSecret)),

    // Transient failures retry inside the SDK — 429s wait out their
    // Retry-After, everything else backs off exponentially. The defaults
    // (3 attempts) are fine; shown here because a background worker can
    // afford to be more patient than an interactive caller.
    Resilience = new OcctooResilienceOptions { MaxRetryAttempts = 5 },
});

// 3. The worker that handles the file events.
builder.Services.AddHostedService<SyncWorker>();

await builder.Build().RunAsync();
