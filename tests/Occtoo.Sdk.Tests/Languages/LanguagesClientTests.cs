using System.Net;
using Occtoo.Authentication;
using Occtoo.Languages;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Languages;

public class LanguagesClientTests
{
    private const string LanguagesBody = """
        { "defaultLanguage": "en", "items": [ { "code": "EN_legacy", "name": null }, { "code": "en", "name": "English" } ] }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Get_keeps_a_stored_code_that_is_not_iso_exactly_as_it_is()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, LanguagesBody);
        using var client = Client(handler);

        var languages = (await client.Languages.Get(TestContext.Current.CancellationToken)).Value;

        languages.Default.GetValueOrThrow().ShouldBe(LanguageCode.From("en"));
        languages.Items.Select(language => language.Code.Value).ShouldBe(["EN_legacy", "en"]);
        languages.Items[0].Name.HasNoValue.ShouldBeTrue();
        languages.Items[1].Name.GetValueOrThrow().ShouldBe("English");
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/languages");
    }

    [Fact]
    public async Task Replace_round_trips_what_was_read_with_the_changes()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, LanguagesBody)
            .Respond(HttpStatusCode.OK, LanguagesBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var read = (await client.Languages.Get(ct)).Value;
        var changed = read.With(LanguageCode.From("sv-SE"), "Swedish").With(LanguageCode.From("en"), "English (UK)").WithDefault(LanguageCode.From("sv-SE"));
        (await client.Languages.Replace(changed, ct)).IsSuccess.ShouldBeTrue();

        var request = handler.Requests[1];
        request.Method.ShouldBe(HttpMethod.Put);
        request.Body.ShouldBe("""{"defaultLanguage":"sv-SE","items":[{"code":"EN_legacy","name":null},{"code":"en","name":"English (UK)"},{"code":"sv-SE","name":"Swedish"}]}""");
    }

    [Fact]
    public async Task A_default_that_is_not_one_of_the_languages_fails_before_the_request()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);
        var languages = new TenantLanguages(LanguageCode.From("de"), [new TenantLanguage(LanguageCode.From("en"), "English")]);

        var result = await client.Languages.Replace(languages, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public void Removing_the_default_language_clears_the_default()
    {
        var languages = new TenantLanguages(LanguageCode.From("en"), [new TenantLanguage(LanguageCode.From("en"), "English")]);

        var without = languages.Without(LanguageCode.From("en"));

        without.Items.ShouldBeEmpty();
        without.Default.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public void Constructing_a_code_stays_strict()
    {
        LanguageCode.TryFrom("EN_legacy").IsSuccess.ShouldBeFalse();
    }
}
