// A worker service that periodically ingests into Occtoo.
//
// Shows the three pieces a real service wires together:
//
//   1. configuration  — the "Occtoo" section of appsettings.json (in a real
//                       deployment, override the secret via the environment:
//                       Occtoo__ClientSecret)
//   2. DI             — AddOcctooClient over IHttpClientFactory, one shared
//                       client and credential for the whole host
//   3. ingest         — a BackgroundService upserting typed entries on a timer
//
// Fill in the placeholders in appsettings.json and `dotnet run`.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Occtoo;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.Sdk.Examples.Sources.Ingest;

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
            Scopes = [OcctooScopes.WriteSources],
        },
        ClientSecret.From(settings.ClientSecret)),

    // Transient failures retry inside the SDK — 429s wait out their
    // Retry-After, everything else backs off exponentially. The defaults
    // (3 attempts) are fine; shown here because a background worker can
    // afford to be more patient than an interactive caller.
    Resilience = new OcctooResilienceOptions { MaxRetryAttempts = 5 },
});

// 3. The periodic ingest itself.
builder.Services.AddHostedService<IngestWorker>();

await builder.Build().RunAsync();
