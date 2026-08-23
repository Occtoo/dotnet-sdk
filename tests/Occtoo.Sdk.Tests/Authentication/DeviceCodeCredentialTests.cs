using System.Net;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Time.Testing;
using Occtoo.Authentication;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests.Authentication;

public class DeviceCodeCredentialTests
{
    private static readonly OcctooAuthorityOptions Authority = new()
    {
        ClientId = ClientId.From("native-client"),
        Audience = Audience.From("tenant-1"),
        Scopes = ["openid", OcctooScopes.Offline],
    };

    private const string RefreshTokenKey = "auth.occtoo.com|native-client";

    private static FusionCache FreshCache() => new(new FusionCacheOptions());

    private const string DeviceAuthorization = """
        {
          "device_code": "device-code-1",
          "user_code": "WXYZ-1234",
          "verification_uri": "https://auth.occtoo.com/activate",
          "verification_uri_complete": "https://auth.occtoo.com/activate?user_code=WXYZ-1234",
          "expires_in": 600,
          "interval": 5
        }
        """;

    private const string Pending = """{"error":"authorization_pending"}""";

    private static string TokenBody(string accessToken, string? refreshToken = "refresh-1") =>
        refreshToken is null
            ? $$"""{"access_token":"{{accessToken}}","expires_in":3600}"""
            : $$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","expires_in":3600}""";

    /// <summary>
    /// Drives the fake clock until the credential stops waiting between polls.
    /// </summary>
    private static async Task<Result<AccessToken, OcctooError>> Complete(
        OcctooTokenCredential credential,
        FakeTimeProvider time,
        CancellationToken cancellationToken)
    {
        var task = credential.GetToken(cancellationToken).AsTask();

        for (var tick = 0; tick < 200 && !task.IsCompleted; tick++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(1, cancellationToken);
        }

        return await task;
    }

    [Fact]
    public async Task Shows_the_user_a_code_then_polls_until_they_approve()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, Pending)
            .Respond(HttpStatusCode.BadRequest, Pending)
            .Respond(HttpStatusCode.OK, TokenBody("user-token"));
        using var httpClient = new HttpClient(handler);

        DeviceCodeInfo? shown = null;
        using var credential = new DeviceCodeCredential(
            Authority,
            (info, _) => { shown = info; return Task.CompletedTask; },
            httpClient: httpClient,
            accessTokenCache: FreshCache())
        {
            TimeProvider = time,
        };

        var token = await Complete(credential, time, TestContext.Current.CancellationToken);

        token.Value.Value.ShouldBe("user-token");
        shown.ShouldNotBeNull();
        shown!.Value.UserCode.ShouldBe("WXYZ-1234");
        shown.Value.VerificationUriComplete.GetValueOrDefault()!.ToString()
            .ShouldBe("https://auth.occtoo.com/activate?user_code=WXYZ-1234");
        shown.Value.Message.ShouldContain("WXYZ-1234");

        var deviceRequest = handler.Requests[0];
        deviceRequest.RequestUri!.ToString()
            .ShouldBe("https://auth.occtoo.com/oauth2/device/auth");
        deviceRequest.Form["client_id"].ShouldBe("native-client");
        deviceRequest.Form["audience"].ShouldBe("tenant-1");
        deviceRequest.Form["scope"].ShouldBe("openid offline");

        var poll = handler.Requests[1];
        poll.RequestUri!.ToString().ShouldBe("https://auth.occtoo.com/oauth2/token");
        poll.Form["grant_type"].ShouldBe("urn:ietf:params:oauth:grant-type:device_code");
        poll.Form["device_code"].ShouldBe("device-code-1");
        // No secret: this is a public client.
        poll.Form.ShouldNotContainKey("client_secret");
    }

    [Fact]
    public async Task Backs_off_when_the_server_says_slow_down()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, """{"error":"slow_down"}""")
            .Respond(HttpStatusCode.OK, TokenBody("user-token"));
        using var httpClient = new HttpClient(handler);
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => Task.CompletedTask,
            httpClient: httpClient,
            accessTokenCache: FreshCache())
        {
            TimeProvider = time,
        };

        var token = await Complete(credential, time, TestContext.Current.CancellationToken);

        token.Value.Value.ShouldBe("user-token");
        handler.RequestCount.ShouldBe(3);
    }

    [Fact]
    public async Task Gives_up_when_the_user_denies_the_request()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.BadRequest, """{"error":"access_denied"}""");
        using var httpClient = new HttpClient(handler);
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => Task.CompletedTask,
            httpClient: httpClient,
            accessTokenCache: FreshCache())
        {
            TimeProvider = time,
        };

        var result = await Complete(credential, time, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<AuthenticationError>();
        error.ErrorCode.GetValueOrDefault().ShouldBe("access_denied");
        error.Message.ShouldContain("denied");
    }

    [Fact]
    public async Task Gives_up_when_the_code_expires_before_approval()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, DeviceAuthorization)
            .RespondAlways(HttpStatusCode.BadRequest, Pending);
        using var httpClient = new HttpClient(handler);
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => Task.CompletedTask,
            httpClient: httpClient,
            accessTokenCache: FreshCache())
        {
            TimeProvider = time,
        };

        var result = await Complete(credential, time, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthenticationError>()
            .ErrorCode.GetValueOrDefault().ShouldBe("expired_token");
    }

    [Fact]
    public async Task Renews_from_a_cached_refresh_token_without_prompting_again()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, TokenBody("renewed", "refresh-2"));
        using var httpClient = new HttpClient(handler);

        var cache = new InMemoryTokenCache();
        await cache.SetRefreshToken(RefreshTokenKey, "refresh-1", TestContext.Current.CancellationToken);

        var prompted = false;
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => { prompted = true; return Task.CompletedTask; },
            cache,
            httpClient,
            FreshCache())
        {
            TimeProvider = time,
        };

        var token = await credential.GetToken(TestContext.Current.CancellationToken);

        token.Value.Value.ShouldBe("renewed");
        prompted.ShouldBeFalse();

        var request = handler.Requests.Single();
        request.Form["grant_type"].ShouldBe("refresh_token");
        request.Form["refresh_token"].ShouldBe("refresh-1");

        // A rotated refresh token replaces the stored one.
        (await cache.GetRefreshToken(RefreshTokenKey, TestContext.Current.CancellationToken))
            .ShouldBe("refresh-2");
    }

    [Fact]
    public async Task Signs_in_again_when_the_stored_refresh_token_is_rejected()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
            .Respond(HttpStatusCode.OK, DeviceAuthorization)
            .Respond(HttpStatusCode.OK, TokenBody("fresh-token", "refresh-3"));
        using var httpClient = new HttpClient(handler);

        var cache = new InMemoryTokenCache();
        await cache.SetRefreshToken(RefreshTokenKey, "revoked", TestContext.Current.CancellationToken);

        var prompted = false;
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => { prompted = true; return Task.CompletedTask; },
            cache,
            httpClient,
            FreshCache())
        {
            TimeProvider = time,
        };

        var token = await Complete(credential, time, TestContext.Current.CancellationToken);

        token.Value.Value.ShouldBe("fresh-token");
        prompted.ShouldBeTrue();
    }

    [Fact]
    public async Task Sign_out_forgets_the_stored_refresh_token()
    {
        var cache = new InMemoryTokenCache();
        await cache.SetRefreshToken(RefreshTokenKey, "refresh-1", TestContext.Current.CancellationToken);

        using var handler = new StubHandler();
        using var httpClient = new HttpClient(handler);
        using var credential = new DeviceCodeCredential(
            Authority,
            (_, _) => Task.CompletedTask,
            cache,
            httpClient,
            FreshCache());

        await credential.SignOut(TestContext.Current.CancellationToken);

        (await cache.GetRefreshToken(RefreshTokenKey, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    private sealed class InMemoryTokenCache : IOcctooTokenCache
    {
        private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

        public ValueTask<string?> GetRefreshToken(string key, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_tokens.GetValueOrDefault(key));

        public ValueTask SetRefreshToken(string key, string refreshToken, CancellationToken cancellationToken)
        {
            _tokens[key] = refreshToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearRefreshToken(string key, CancellationToken cancellationToken)
        {
            _tokens.Remove(key);
            return ValueTask.CompletedTask;
        }
    }
}
