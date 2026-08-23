using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Occtoo.Authentication;
using Occtoo.Logging;
using Occtoo.Sources;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests.Common.Logging;

public class OcctooLogTests
{
    private static readonly OcctooAuthorityOptions Authority = new()
    {
        ClientId = ClientId.From("client-abc"),
        Audience = Audience.From("tenant-1"),
    };

    private const string AcceptedBody = """
        {
          "correlationId": "83b538a7-df7c-4cf4-b988-cd3b71c4cd90",
          "sourceId": "products",
          "acceptedAt": "2026-08-13T10:15:30Z",
          "acceptedEntryCount": 1,
          "newPropertiesFound": []
        }
        """;

    [Fact]
    public async Task Logs_the_meaningful_lifecycle_under_the_occtoo_categories()
    {
        var log = new CollectingLoggerFactory();

        // One transport serves the token endpoint and the API: token, ingest
        // accepted, then a 401 that triggers the refresh-and-retry path.
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}""")
            .Respond(HttpStatusCode.Accepted, AcceptedBody)
            .Respond(HttpStatusCode.Unauthorized, "{}")
            .Respond(HttpStatusCode.OK, """{"access_token":"token-2","expires_in":3600}""")
            .Respond(HttpStatusCode.Accepted, AcceptedBody);

        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(
            Authority, ClientSecret.From("secret"), tokenClient, new FusionCache(new FusionCacheOptions()));

        using var httpClient = new HttpClient(
            new Occtoo.Http.OcctooAuthenticationHandler(
                credential,
                retryOnUnauthorized: true,
                log.CreateLogger(OcctooLogCategories.Http))
            { InnerHandler = transport });

        using var client = new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = credential,
            LoggerFactory = log,
        });

        var entry = SourceEntry.WithId("sku-1").WithText("name", "chair").Build();

        (await client.Sources.IngestEntries(SourceId.From("products"), [entry], TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        (await client.Sources.IngestEntries(SourceId.From("products"), [entry], TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();

        // Token retrieval: a miss narrated at Debug, the acquisition at Information.
        log.Entries.ShouldContain(e => e.Category == "Occtoo.Authentication"
            && e.Level == LogLevel.Debug && e.Message.Contains("missing"));
        log.Entries.ShouldContain(e => e.Category == "Occtoo.Authentication"
            && e.Level == LogLevel.Information && e.Message.Contains("Access token acquired"));

        // The revoked-token recovery is a Warning under Occtoo.Http.
        log.Entries.ShouldContain(e => e.Category == "Occtoo.Http"
            && e.Level == LogLevel.Warning && e.Message.Contains("retrying once"));

        // Each accepted batch logs its correlation id under Occtoo.Sources.
        log.Entries.Count(e => e.Category == "Occtoo.Sources"
            && e.Level == LogLevel.Information
            && e.Message.Contains("83b538a7-df7c-4cf4-b988-cd3b71c4cd90")).ShouldBe(2);
    }

    [Fact]
    public async Task Logs_a_rejected_ingest_as_a_warning()
    {
        var log = new CollectingLoggerFactory();
        using var transport = new StubHandler().Respond(HttpStatusCode.Forbidden, """{"title":"no scope"}""");
        using var httpClient = new HttpClient(transport);
        using var client = new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            LoggerFactory = log,
        });

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"),
            [SourceEntry.WithId("sku-1").WithText("name", "chair").Build()],
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        log.Entries.ShouldContain(e => e.Category == "Occtoo.Sources"
            && e.Level == LogLevel.Warning && e.Message.Contains("ForbiddenError"));
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CollectingLoggerFactory : ILoggerFactory
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CollectingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
