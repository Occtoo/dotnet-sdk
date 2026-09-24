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
            .WithApiVersions("2B1D7F3A-5C2E-4B8F-9A6D-1E0C4F7A8B9C")
            .Build();

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

        var update = current.Edit().WithSources("assets").Build();

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

    [Fact]
    public void Edit_can_revoke_grants_and_clear_metadata()
    {
        var current = new Application(
            TenantApplicationId.From(Guid.NewGuid()), "Reader", "Old", ClientId.From("client-abc"),
            ["commerce", "legacy"], ["read:sources", "read:events"], ["sources", "source:products"],
            ["destinations", "destination:webshop"], [], Etag: 4, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var update = current.Edit()
            .WithoutDescription()
            .WithoutTags("legacy")
            .WithoutScopes(OcctooScopes.ReadEvents)
            .WithoutAllSources()
            .WithoutDestinations("webshop")
            .Build();

        update.Etag.ShouldBe(4u);
        update.Description.HasNoValue.ShouldBeTrue();
        update.Tags.ShouldBe(["commerce"]);
        update.ScopeKeys.ShouldBe(["read:sources"]);
        update.ResourceSelectors.ShouldBe(["source:products"]);
        update.ApiSelectors.ShouldBe(["destinations"]);
    }

    private static Application ProductReader() => new(
        TenantApplicationId.From(Guid.NewGuid()), "Reader", "Old", ClientId.From("client-abc"),
        [], [OcctooScopes.ReadSources], ["source:products"], [], [], Etag: 2,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Removing_the_last_source_is_refused_because_the_api_would_grant_every_source()
    {
        var editor = ProductReader().Edit().WithoutSources("products");

        Should.Throw<InvalidOperationException>(() => editor.Build()).Message.ShouldContain("every source");
    }

    [Fact]
    public void Removing_the_read_scope_is_refused_because_the_api_would_grant_write()
    {
        var editor = ProductReader().Edit().WithoutScopes(OcctooScopes.ReadSources);

        Should.Throw<InvalidOperationException>(() => editor.Build()).Message.ShouldContain("write access");
    }

    [Fact]
    public void Revoking_source_access_entirely_is_allowed()
    {
        var update = ProductReader().Edit()
            .WithoutSources("products")
            .WithoutScopes(OcctooScopes.ReadSources)
            .Build();

        update.ScopeKeys.ShouldBeEmpty();
        update.ResourceSelectors.ShouldBeEmpty();
    }

    [Fact]
    public void Swapping_one_source_for_another_is_allowed()
    {
        var update = ProductReader().Edit().WithoutSources("products").WithSources("assets").Build();

        update.ResourceSelectors.ShouldBe(["source:assets"]);
    }
}
