using System.Net;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests;

public class OcctooClientTests
{
    private static readonly OcctooAuthorityOptions Authority = new()
    {
        ClientId = ClientId.From("client-abc"),
        Audience = Audience.From("tenant-1"),
    };

    private static readonly ClientSecret Secret = ClientSecret.From("secret");

    private static FusionCache FreshCache() => new(new FusionCacheOptions());

    [Fact]
    public void Defaults_to_the_public_api()
    {
        using var client = new OcctooClient(new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

        client.BaseAddress.ShouldBe(new Uri("https://api.occtoo.com"));
    }

    [Fact]
    public void Rejects_a_relative_base_address()
    {
        var options = new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            BaseAddress = new Uri("/v1", UriKind.Relative),
        };

        Should.Throw<InvalidOperationException>(() => new OcctooClient(options))
            .Message.ShouldContain("absolute");
    }

    [Fact]
    public void Rejects_a_missing_credential()
    {
        var options = new OcctooClientOptions { Credential = null! };

        Should.Throw<InvalidOperationException>(() => new OcctooClient(options))
            .Message.ShouldContain("Credential");
    }

    [Fact]
    public async Task Authenticate_establishes_a_token_credential_up_front()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}""");
        using var tokenClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());

        using var client = new OcctooClient(new OcctooClientOptions { Credential = credential });

        var result = await client.Authenticate(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.GetValueOrDefault().Value.ShouldBe("token-1");
    }

    [Fact]
    public async Task Authenticate_reports_no_token_for_an_api_key()
    {
        // An API key cannot be verified without calling an API, so there is
        // nothing to return — and nothing to fail on either.
        using var client = new OcctooClient(new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

        var result = await client.Authenticate(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Authenticate_surfaces_a_rejected_secret_at_startup()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        using var tokenClient = new HttpClient(handler);
        using var credential = new ClientCredentialsCredential(Authority, Secret, tokenClient, FreshCache());

        using var client = new OcctooClient(new OcctooClientOptions { Credential = credential });

        var result = await client.Authenticate(TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<AuthenticationError>();
    }

    [Fact]
    public void Does_not_dispose_an_http_client_it_was_given()
    {
        using var handler = new StubHandler();
        using var httpClient = new HttpClient(handler);
        var client = new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });
        client.Dispose();

        // Still usable: disposing the SDK client must not break the caller's client.
        httpClient.BaseAddress.ShouldBe(new Uri("https://api.occtoo.com"));
    }

    [Fact]
    public async Task From_delegate_is_asked_again_once_its_token_goes_stale()
    {
        var calls = 0;
        using var credential = OcctooCredential.FromDelegate(_ =>
        {
            calls++;
            return ValueTask.FromResult(Result.Success<AccessToken, OcctooError>(
                new AccessToken($"token-{calls}", DateTimeOffset.UtcNow.AddSeconds(-1))));
        });

        await credential.GetToken(TestContext.Current.CancellationToken);
        await credential.GetToken(TestContext.Current.CancellationToken);

        calls.ShouldBe(2);
    }

    [Fact]
    public void Dependency_injection_registers_a_shared_client_and_credential()
    {
        var services = new ServiceCollection();

        services.AddOcctooClient(options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            BaseAddress = new Uri("https://api.occtoo.com"),
        });

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<OcctooClient>();
        var second = provider.GetRequiredService<OcctooClient>();

        // One credential instance, or token caching would not be shared.
        first.ShouldBeSameAs(second);
        first.BaseAddress.ShouldBe(new Uri("https://api.occtoo.com"));
    }

    [Fact]
    public void Dependency_injection_rejects_options_with_no_credential()
    {
        var services = new ServiceCollection();
        services.AddOcctooClient(options => options);

        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<OcctooClient>());
    }

    [Fact]
    public async Task Dependency_injection_authenticates_requests_made_through_the_named_client()
    {
        var services = new ServiceCollection();
        services.AddOcctooClient(options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

        var transport = new StubHandler().RespondAlways(HttpStatusCode.OK, "{}");
        services
            .AddHttpClient(OcctooServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => transport);

        using var provider = services.BuildServiceProvider();
        var httpClient = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(OcctooServiceCollectionExtensions.HttpClientName);

        var response = await httpClient.GetAsync(
            new Uri("https://api.occtoo.com/v1/events"),
            TestContext.Current.CancellationToken);
        response.Dispose();

        transport.Requests.Single().Header("x-api-key").ShouldBe("key-1");
    }

    [Fact]
    public async Task A_throwing_delegate_callback_is_a_result_not_an_exception()
    {
        using var credential = OcctooCredential.FromDelegate(
            _ => throw new InvalidOperationException("vault unreachable"));

        var result = await credential.GetToken(TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>().Message.ShouldContain("vault unreachable");
    }

    [Fact]
    public void Keyed_registrations_with_colliding_key_strings_stay_separate()
    {
        // Two sentinel objects share ToString() ("System.Object"); they must
        // still get independent pipelines, or one tenant's auth handler would
        // stack onto the other's.
        var services = new ServiceCollection();
        object keyA = new(), keyB = new();

        services.AddKeyedOcctooClient(keyA, options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-a")),
            BaseAddress = new Uri("https://api.a.occtoo.com"),
        });
        services.AddKeyedOcctooClient(keyB, options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-b")),
            BaseAddress = new Uri("https://api.b.occtoo.com"),
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<OcctooClient>(keyA)
            .BaseAddress.ShouldBe(new Uri("https://api.a.occtoo.com"));
        provider.GetRequiredKeyedService<OcctooClient>(keyB)
            .BaseAddress.ShouldBe(new Uri("https://api.b.occtoo.com"));
    }

    [Fact]
    public void Keyed_registration_gives_each_key_its_own_client()
    {
        var services = new ServiceCollection();

        services.AddKeyedOcctooClient("europe", options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-eu")),
            BaseAddress = new Uri("https://api.eu.occtoo.com"),
        });
        services.AddKeyedOcctooClient("americas", options => options with
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-us")),
            BaseAddress = new Uri("https://api.us.occtoo.com"),
        });

        using var provider = services.BuildServiceProvider();

        var europe = provider.GetRequiredKeyedService<OcctooClient>("europe");
        var americas = provider.GetRequiredKeyedService<OcctooClient>("americas");

        europe.ShouldNotBeSameAs(americas);
        europe.BaseAddress.ShouldBe(new Uri("https://api.eu.occtoo.com"));
        americas.BaseAddress.ShouldBe(new Uri("https://api.us.occtoo.com"));

        // The same key resolves the same instance, so token caching is shared.
        provider.GetRequiredKeyedService<OcctooClient>("europe").ShouldBeSameAs(europe);
    }
}
