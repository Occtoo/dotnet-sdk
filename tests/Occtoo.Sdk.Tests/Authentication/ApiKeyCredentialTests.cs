using Occtoo.Authentication;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Authentication;

public class ApiKeyCredentialTests
{
    [Fact]
    public async Task Sends_the_key_in_the_x_api_key_header()
    {
        var credential = OcctooCredential.ApiKey(ApiKey.From("key-123"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.occtoo.com/v1/events");

        var applied = await credential.Apply(request, TestContext.Current.CancellationToken);

        applied.IsSuccess.ShouldBeTrue();
        request.Headers.GetValues(ApiKeyCredential.HeaderName).ShouldBe(["key-123"]);
        request.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Does_not_duplicate_the_header_when_a_request_is_retried()
    {
        var credential = OcctooCredential.ApiKey(ApiKey.From("key-123"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.occtoo.com/v1/events");

        await credential.Apply(request, TestContext.Current.CancellationToken);
        await credential.Apply(request, TestContext.Current.CancellationToken);

        request.Headers.GetValues(ApiKeyCredential.HeaderName).ShouldBe(["key-123"]);
    }

    [Fact]
    public void Rejects_an_empty_key_at_construction()
    {
        Should.Throw<ValueObjectValidationException>(() => ApiKey.From("  "));
    }

    [Fact]
    public async Task A_null_request_is_a_result_not_an_exception()
    {
        var credential = OcctooCredential.ApiKey(ApiKey.From("key-123"));

        var applied = await credential.Apply(null!, TestContext.Current.CancellationToken);

        applied.Error.ShouldBeOfType<ValidationError>();
    }

    [Fact]
    public void Never_prints_the_key()
    {
        $"{ApiKey.From("key-123")}".ShouldNotContain("key-123");
    }
}
