using System.Net;
using Occtoo.Applications;
using Occtoo.Authentication;
using Shouldly;
using Xunit;
namespace Occtoo.Sdk.Tests.Common.Http;

// Responses that parse but lack required values surface as results, never exceptions.
public class MalformedResponseTests
{
    private static OcctooClient Client(StubHandler h) => new(new HttpClient(h), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("k")) });
    [Theory]
    [InlineData("create", """{ "application": null, "clientSecret": "s" }""")]
    [InlineData("sources", """{ "items": null }""")]
    [InlineData("apps", "{}")]
    public async Task Is_an_unexpected_error(string op, string body)
    {
        using var h = new StubHandler().Respond(HttpStatusCode.OK, body);
        using var c = Client(h);
        OcctooError? error = op switch
        {
            "create" => (await c.Applications.Create(CreateApplication.WithName("x"), TestContext.Current.CancellationToken)).Error,
            "sources" => (await c.Sources.List(cancellationToken: TestContext.Current.CancellationToken)).Error,
            _ => (await c.Applications.List(cancellationToken: TestContext.Current.CancellationToken)).Error,
        };
        error.ShouldBeOfType<UnexpectedError>();
    }
}
