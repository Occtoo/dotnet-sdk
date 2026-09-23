using Occtoo.Applications;
using Occtoo.Authentication;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Applications;

public class ApplicationBuilderTests
{
    [Fact]
    public void Spells_every_grant_the_way_the_api_expects()
    {
        CreateApplication application = CreateApplication.WithName("Catalog reader")
            .WithDescription("Reads products")
            .WithTags("commerce")
            .WithScopes(OcctooScopes.ReadSources, OcctooScopes.ReadEvents)
            .WithSources("products", "assets")
            .WithDestinations("webshop")
            .WithApiVersions("2B1D7F3A-5C2E-4B8F-9A6D-1E0C4F7A8B9C");

        application.Name.ShouldBe("Catalog reader");
        application.Description.GetValueOrThrow().ShouldBe("Reads products");
        application.Tags.ShouldBe(["commerce"]);
        application.ScopeKeys.ShouldBe(["read:sources", "read:events"]);
        application.ResourceSelectors.ShouldBe(["source:products", "source:assets"]);
        application.ApiSelectors.ShouldBe(["destination:webshop", "api-version:2b1d7f3a5c2e4b8f9a6d1e0c4f7a8b9c"]);
    }

    [Fact]
    public void Aggregate_grants_and_duplicates()
    {
        var application = CreateApplication.WithName("Everything")
            .WithAllSources()
            .WithAllDestinations()
            .WithScopes(OcctooScopes.WriteSources, OcctooScopes.WriteSources)
            .Build();

        application.ResourceSelectors.ShouldBe(["sources"]);
        application.ApiSelectors.ShouldBe(["destinations"]);
        application.ScopeKeys.ShouldBe(["write:sources"]);
    }

    [Fact]
    public void Edit_starts_from_the_current_settings_and_carries_the_etag_into_the_update()
    {
        var current = new Application(
            TenantApplicationId.From(Guid.NewGuid()), "Reader", "Old", ClientId.From("client-abc"),
            ["commerce"], ["read:sources"], ["source:products"], [], [], Etag: 7,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var update = current.Edit().WithSources("assets").BuildUpdate(current.Etag);

        update.Name.ShouldBe("Reader");
        update.Etag.ShouldBe(7u);
        update.Tags.ShouldBe(["commerce"]);
        update.ScopeKeys.ShouldBe(["read:sources"]);
        update.ResourceSelectors.ShouldBe(["source:products", "source:assets"]);
    }

    [Fact]
    public void Invalid_inputs_fail_at_the_call_site()
    {
        Should.Throw<ValueObjectValidationException>(() => CreateApplication.WithName(" "));
        Should.Throw<ValueObjectValidationException>(() => CreateApplication.WithName(new string('a', 101)));
        var builder = CreateApplication.WithName("Reader");
        Should.Throw<ValueObjectValidationException>(() => builder.WithDescription(new string('a', 501)));
        Should.Throw<ValueObjectValidationException>(() => builder.WithTags(""));
        Should.Throw<ValueObjectValidationException>(() => builder.WithScopes("read:sources write:sources"));
        Should.Throw<ValueObjectValidationException>(() => builder.WithSources(""));
    }
}
