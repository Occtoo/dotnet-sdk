using System.Net;
using Occtoo.Authentication;
using Occtoo.Http;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests.Common.Http;

public class OcctooAuthenticationHandlerTests
{
    private static readonly OcctooAuthorityOptions Authority = new()
    {
        ClientId = ClientId.From("client-abc"),
        Audience = Audience.From("tenant-1"),
    };

    private static readonly ClientSecret Secret = ClientSecret.From("secret");

    private static FusionCache FreshCache() => new(new FusionCacheOptions());

    [Fact]
    public async Task Authenticates_every_request()
    {
        using var transport = new StubHandler().RespondAlways(HttpStatusCode.OK, "{}");
        using var handler = new OcctooAuthenticationHandler(OcctooCredential.ApiKey(ApiKey.From("key-1")))
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/events"),
            TestContext.Current.CancellationToken);
        await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/event-types"),
            TestContext.Current.CancellationToken);

        transport.Requests.ShouldAllBe(request => request.Header("x-api-key") == "key-1");
    }

    [Fact]
    public async Task Replaces_a_revoked_token_and_retries_once()
    {
        // The token endpoint and the API share one transport here; the token
        // requests are the POSTs to the authorization server.
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"stale","expires_in":3600}""")
            .Respond(HttpStatusCode.Unauthorized, "{}")
            .Respond(HttpStatusCode.OK, """{"access_token":"fresh","expires_in":3600}""")
            .Respond(HttpStatusCode.OK, """{"ok":true}""");

        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());
        using var handler = new OcctooAuthenticationHandler(credential)
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/events"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Dispose();

        transport.RequestCount.ShouldBe(4);
        transport.Requests[1].Header("Authorization").ShouldBe("Bearer stale");
        transport.Requests[3].Header("Authorization").ShouldBe("Bearer fresh");
    }

    [Fact]
    public async Task Does_not_retry_when_the_caller_turned_it_off()
    {
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"stale","expires_in":3600}""")
            .Respond(HttpStatusCode.Unauthorized, "{}");

        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());
        using var handler = new OcctooAuthenticationHandler(credential, retryOnUnauthorized: false)
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/events"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Dispose();
        transport.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task Does_not_retry_an_api_key_that_the_server_rejects()
    {
        // Nothing to refresh: retrying would just repeat the same rejection.
        using var transport = new StubHandler().RespondAlways(HttpStatusCode.Unauthorized, "{}");
        using var handler = new OcctooAuthenticationHandler(OcctooCredential.ApiKey(ApiKey.From("key-1")))
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        var response = await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/events"),
            TestContext.Current.CancellationToken);

        response.Dispose();
        transport.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Resends_the_body_when_it_retries_a_post()
    {
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"stale","expires_in":3600}""")
            .Respond(HttpStatusCode.Unauthorized, "{}")
            .Respond(HttpStatusCode.OK, """{"access_token":"fresh","expires_in":3600}""")
            .Respond(HttpStatusCode.Accepted, """{"ok":true}""");

        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());
        using var handler = new OcctooAuthenticationHandler(credential)
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        using var content = new StringContent(
            """{"entries":[]}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(
            new Uri("https://api.occtoo.com/v1/sources/products"),
            content,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Dispose();

        // The retried request carried the same body, not an empty one.
        transport.Requests[3].Body.ShouldBe("""{"entries":[]}""");
    }

    [Fact]
    public async Task Surfaces_a_failed_credential_as_a_typed_exception_for_raw_http_client_users()
    {
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");

        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());
        using var handler = new OcctooAuthenticationHandler(credential)
        {
            InnerHandler = transport,
        };
        using var httpClient = new HttpClient(handler);

        var exception = await Should.ThrowAsync<OcctooCredentialException>(
            async () => await httpClient.GetAsync(
                new Uri("https://api.occtoo.com/v1/events"),
                TestContext.Current.CancellationToken));

        exception.Error.ShouldBeOfType<AuthenticationError>()
            .ErrorCode.GetValueOrDefault().ShouldBe("invalid_client");
    }
}
