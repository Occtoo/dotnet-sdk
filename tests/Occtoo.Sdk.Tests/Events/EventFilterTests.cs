using Occtoo.Events;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventFilterTests
{
    [Fact]
    public void Renders_a_single_type_with_no_conditions()
    {
        EventFilter.OfType<SourceEntryAdded>().ToString()
            .ShouldBe("""type eq "source_entry.added" """.TrimEnd());
    }

    [Fact]
    public void Renders_a_type_condition_pair_joined_with_and()
    {
        EventFilter.OfType<SourceEntryAdded>(e => e.WithSource("products")).ToString()
            .ShouldBe("""type eq "source_entry.added" and sourceId eq "products" """.TrimEnd());
    }

    [Fact]
    public void Renders_several_values_of_one_condition_as_a_parenthesized_or()
    {
        EventFilter.OfType<SourceEntryAdded>(e => e.WithSource("products", "assets")).ToString()
            .ShouldBe("""type eq "source_entry.added" and (sourceId eq "products" or sourceId eq "assets")""");
    }

    [Fact]
    public void Chains_distinct_conditions_with_and()
    {
        EventFilter
            .OfType<CardSegmentMembershipChanged>(e => e
                .WithCardDefinition("cards")
                .WithSegment("summer-sale"))
            .ToString()
            .ShouldBe(
                """type eq "card.segment_membership_changed" and cardDefinitionId eq "cards" and segmentId eq "summer-sale" """
                    .TrimEnd());
    }

    [Fact]
    public void Groups_alternative_types_with_or_and_parentheses()
    {
        EventFilter
            .OfType<SourceEntryAdded>(e => e.WithSource("products"))
            .OrType<SegmentUpdated>(e => e.WithSegment("summer-sale"))
            .ToString()
            .ShouldBe(
                """(type eq "source_entry.added" and sourceId eq "products") or (type eq "segment.updated" and segmentId eq "summer-sale")""");
    }

    [Fact]
    public void Expands_a_family_base_to_every_member_type()
    {
        EventFilter.OfType<SourceEntryEvent>().ToString()
            .ShouldBe(
                """(type eq "source_entry.added" or type eq "source_entry.deleted" or type eq "source_entry.updated")""");
    }

    [Fact]
    public void Combines_a_family_base_with_conditions_shared_by_all_members()
    {
        EventFilter.OfType<SourceEntryEvent>(e => e.WithSource("products")).ToString()
            .ShouldBe(
                """(type eq "source_entry.added" or type eq "source_entry.deleted" or type eq "source_entry.updated") and sourceId eq "products" """
                    .TrimEnd());
    }

    [Fact]
    public void Escapes_quotes_and_backslashes_in_values()
    {
        EventFilter
            .OfType<DestinationEntryUpdated>(e => e.WithApiVersionName("""v1 "beta" \ latest"""))
            .ToString()
            .ShouldBe(
                """type eq "destination_entry.updated" and apiVersion eq "v1 \"beta\" \\ latest" """.TrimEnd());
    }

    [Fact]
    public void Filters_destination_entry_updates_by_endpoint_safe_name()
    {
        EventFilter.OfType<DestinationEntryUpdated>(e => e.WithEndpointName("products")).ToString()
            .ShouldBe("""type eq "destination_entry.updated" and endpoint eq "products" """.TrimEnd());
    }

    [Fact]
    public void Filters_destination_entry_deletions_by_multiple_endpoint_safe_names()
    {
        EventFilter.OfType<DestinationEntryDeleted>(e => e.WithEndpointName("products", "categories")).ToString()
            .ShouldBe("""type eq "destination_entry.deleted" and (endpoint eq "products" or endpoint eq "categories")""");
    }

    [Fact]
    public void Scopes_endpoint_names_to_a_destination_across_api_versions()
    {
        EventFilter.OfType<DestinationEntryEvent>(e => e
                .WithDestination("destination-id")
                .WithEndpointName("products", "categories"))
            .ToString()
            .ShouldBe(
                """(type eq "destination_entry.deleted" or type eq "destination_entry.updated") and destinationId eq "destination-id" and (endpoint eq "products" or endpoint eq "categories")""");
    }

    [Fact]
    public void Combines_endpoint_names_with_multiple_api_version_numbers()
    {
        EventFilter.OfType<DestinationEntryUpdated>(e => e
                .WithEndpointName("products")
                .WithApiVersionName("2", "3"))
            .ToString()
            .ShouldBe(
                """type eq "destination_entry.updated" and endpoint eq "products" and (apiVersion eq "2" or apiVersion eq "3")""");
    }

    [Fact]
    public void Preserves_endpoint_name_case_and_escapes_quotes_and_backslashes()
    {
        EventFilter.OfType<DestinationEntryUpdated>(e => e.WithEndpointName("""Products "quoted" \ name"""))
            .ToString()
            .ShouldBe("""type eq "destination_entry.updated" and endpoint eq "Products \"quoted\" \\ name" """.TrimEnd());
    }

    [Fact]
    public void Endpoint_id_filters_keep_their_existing_meaning()
    {
        EventFilter.OfType<DestinationEntryUpdated>(e => e.WithEndpoint("endpoint-id")).ToString()
            .ShouldBe("""type eq "destination_entry.updated" and endpointId eq "endpoint-id" """.TrimEnd());
        EventFilter.OfType<EndpointCreated>(e => e.WithEndpoint("endpoint-id")).ToString()
            .ShouldBe("""type eq "endpoint.created" and endpointId eq "endpoint-id" """.TrimEnd());
    }

    [Theory]
    [InlineData(typeof(EndpointEvent))]
    [InlineData(typeof(EndpointCreated))]
    [InlineData(typeof(EndpointUpdated))]
    [InlineData(typeof(EndpointDeleted))]
    [InlineData(typeof(DestinationEvent))]
    [InlineData(typeof(SourceEntryUpdated))]
    public void Endpoint_name_condition_rejects_types_without_the_capability(Type eventType)
    {
        var method = typeof(EventFilterConditions).GetMethod(nameof(EventFilterConditions.WithEndpointName))!;

        Should.Throw<ArgumentException>(() => method.MakeGenericMethod(eventType));
    }

    [Fact]
    public void Raw_filters_pass_through_verbatim()
    {
        EventFilter.Raw("""type eq "source.created" and sourceId eq "x" """).ToString()
            .ShouldBe("""type eq "source.created" and sourceId eq "x" """);
    }

    [Fact]
    public void Raw_rejects_blank_filters()
    {
        Should.Throw<ArgumentException>(() => EventFilter.Raw("  "));
    }

    [Fact]
    public void Condition_values_go_through_identifier_validation()
    {
        Should.Throw<ValueObjectValidationException>(() =>
            EventFilter.OfType<SourceEntryAdded>(e => e.WithSource(" ")));
    }

    // The point of the builder is what does NOT compile. These stay as a
    // documented negative: `EventFilter.OfType<CardDefinitionUpdated>(e =>
    // e.WithSource("products"))` fails with CS0311 because
    // CardDefinitionUpdated does not implement IFilterableBySource — the
    // invalid type/property combination the API would answer with a 400.
}
