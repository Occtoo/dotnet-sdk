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
