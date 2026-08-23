using System.Net;
using Microsoft.Extensions.Time.Testing;
using Occtoo.Authentication;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests.Authentication;

public class ClientCredentialsCredentialTests
{
    private static readonly OcctooAuthorityOptions Authority = new()
    {
        ClientId = ClientId.From("client-abc"),
        Audience = Audience.From("tenant-1"),
        Scopes = [OcctooScopes.WriteSources],
    };

    private static readonly ClientSecret Secret = ClientSecret.From("secret");

    // Each test gets its own cache: the default cache is shared process-wide,
    // and identical credentials would otherwise satisfy each other's tests.
    private static FusionCache FreshCache() => new(new FusionCacheOptions());

    private static string TokenBody(string accessToken, int expiresIn = 3600) =>
        $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"bearer"}""";

    [Fact]
    public async Task Exchanges_the_secret_for_a_token_at_the_default_authority()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.occtoo.com/v1/events");
        var applied = await credential.Apply(request, TestContext.Current.CancellationToken);

        applied.IsSuccess.ShouldBeTrue();
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("token-1");

        var tokenRequest = handler.Requests.Single();
        tokenRequest.Method.ShouldBe(HttpMethod.Post);
        tokenRequest.RequestUri!.ToString().ShouldBe("https://auth.occtoo.com/oauth2/token");
        tokenRequest.Form["grant_type"].ShouldBe("client_credentials");
        tokenRequest.Form["client_id"].ShouldBe("client-abc");
        tokenRequest.Form["client_secret"].ShouldBe("secret");
        tokenRequest.Form["audience"].ShouldBe("tenant-1");
        tokenRequest.Form["scope"].ShouldBe("write:sources");
    }

    [Fact]
    public async Task Honours_an_overridden_authority()
    {
        var authority = Authority with { Authority = new Uri("https://auth.example.occtoo.com") };
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(authority, Secret, httpClient, FreshCache());

        await credential.GetToken(TestContext.Current.CancellationToken);

        handler.Requests.Single().RequestUri!.Host.ShouldBe("auth.example.occtoo.com");
    }

    [Fact]
    public async Task Reuses_the_token_instead_of_calling_the_rate_limited_endpoint_again()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        for (var i = 0; i < 5; i++)
            (await credential.GetToken(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Acquires_a_new_token_only_once_when_many_requests_race_for_it()
    {
        using var handler = new StubHandler().RespondAlways(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
            await credential.GetToken(TestContext.Current.CancellationToken)));

        handler.RequestCount.ShouldBe(1);
        tokens.Select(token => token.Value.Value).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Replaces_the_token_shortly_before_it_expires()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, TokenBody("token-1", expiresIn: 3600))
            .Respond(HttpStatusCode.OK, TokenBody("token-2", expiresIn: 3600));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache())
        {
            TimeProvider = time,
            RefreshSkew = TimeSpan.FromMinutes(1),
        };

        var first = await credential.GetToken(TestContext.Current.CancellationToken);
        first.Value.Value.ShouldBe("token-1");

        // Still comfortably valid.
        time.Advance(TimeSpan.FromMinutes(50));
        (await credential.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");

        // Inside the refresh skew: replaced before it can fail mid-request.
        time.Advance(TimeSpan.FromMinutes(9));
        (await credential.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-2");

        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task Reports_the_oauth_error_when_the_secret_is_wrong()
    {
        using var handler = new StubHandler().Respond(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"Client authentication failed"}""");
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        var result = await credential.GetToken(TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<AuthenticationError>();
        error.ErrorCode.GetValueOrDefault().ShouldBe("invalid_client");
        error.Message.ShouldContain("invalid_client");
        error.Message.ShouldContain("Client authentication failed");
    }

    [Fact]
    public async Task Explains_an_unreachable_authorization_server()
    {
        using var handler = new StubHandler().Respond(
            _ => throw new HttpRequestException("no such host"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        var result = await credential.GetToken(TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        // Unreachable is transient: the caller can retry with backoff.
        var error = result.Error.ShouldBeOfType<NetworkError>();
        error.Message.ShouldContain("auth.occtoo.com");
    }

    [Fact]
    public async Task A_token_without_expires_in_is_kept_not_refetched_per_request()
    {
        // expires_in is optional; its absence deserializes as 0, which must
        // mean "unknown lifetime", not "already expired".
        using var handler = new StubHandler().Respond(
            HttpStatusCode.OK, """{"access_token":"token-1","token_type":"bearer"}""");
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        for (var i = 0; i < 5; i++)
            (await credential.GetToken(TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_5xx_with_an_oauth_error_body_is_still_transient()
    {
        using var handler = new StubHandler().Respond(
            HttpStatusCode.ServiceUnavailable,
            """{"error":"temporarily_unavailable"}""");
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        var result = await credential.GetToken(TestContext.Current.CancellationToken);

        // The status outranks the body: retrying is correct, "check the
        // credential" is not.
        result.Error.ShouldBeOfType<ServerError>().ShouldBeAssignableTo<TransientError>();
    }

    [Fact]
    public async Task Invalidate_if_current_keeps_a_token_that_was_already_replaced()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        (await credential.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");

        // A concurrent 401 for an older token must not evict the fresh one.
        await credential.InvalidateIfCurrent("stale-token", TestContext.Current.CancellationToken);

        (await credential.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Does_not_cache_a_failure()
    {
        using var handler = new StubHandler()
            .Respond(_ => throw new HttpRequestException("blip"))
            .Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        (await credential.GetToken(TestContext.Current.CancellationToken)).IsFailure.ShouldBeTrue();
        (await credential.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");
    }

    [Fact]
    public async Task Rejects_a_success_response_that_carries_no_token()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """{"expires_in":3600}""");
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, httpClient, FreshCache());

        var result = await credential.GetToken(TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnexpectedError>().Message.ShouldContain("no access token");
    }

    [Fact]
    public async Task Omits_scope_when_none_is_configured()
    {
        var authority = Authority with { Scopes = [] };
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(authority, Secret, httpClient, FreshCache());

        await credential.GetToken(TestContext.Current.CancellationToken);

        handler.Requests.Single().Form.ShouldNotContainKey("scope");
    }

    [Fact]
    public async Task Two_credentials_for_the_same_identity_share_a_cached_token()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("token-1"));
        using var httpClient = new HttpClient(handler);
        var cache = FreshCache();
        using var first = new ClientCredentialsCredential(Authority, Secret, httpClient, cache);
        using var second = new ClientCredentialsCredential(Authority, Secret, httpClient, cache);

        (await first.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");
        (await second.GetToken(TestContext.Current.CancellationToken)).Value.Value.ShouldBe("token-1");

        handler.RequestCount.ShouldBe(1);
    }
}
