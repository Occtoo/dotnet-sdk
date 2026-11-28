using System.Text.Json;
using Occtoo.Cards;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Cards;

public class WhereTests
{
    [Fact]
    public void Leaves_render_one_branch_with_one_operation()
    {
        Where.Text("brand").IgnoringCase().Eq("Acme").ToString()
            .ShouldBe("""{"text":{"property":"brand","eq":"Acme","ignoreCase":true}}""");
        Where.Text("title").InLanguages(LanguageCode.From("en"), LanguageCode.From("sv-SE")).Contains("chair").ToString()
            .ShouldBe("""{"text":{"property":"title","contains":"chair","languages":["en","sv-SE"]}}""");
        Where.Text("sku").Length(CountRange.Between(3, 12)).ToString()
            .ShouldBe("""{"text":{"property":"sku","length":{"gte":3,"lte":12}}}""");
        Where.List("colors").Only("red", "blue").ToString()
            .ShouldBe("""{"list":{"property":"colors","only":["red","blue"]}}""");
        Where.Boolean("active").Eq(true).ToString()
            .ShouldBe("""{"boolean":{"property":"active","eq":true}}""");
        Where.Decimal("price").In(9.5m, 10m).ToString()
            .ShouldBe("""{"decimal":{"property":"price","in":[9.5,10]}}""");
        Where.HasValue("image").ToString()
            .ShouldBe("""{"hasValue":{"property":"image"}}""");
        Where.InSegment("campaigns", "summer26").ToString()
            .ShouldBe("""{"segment":{"definitionId":"campaigns","id":"summer26"}}""");
    }

    [Fact]
    public void A_range_takes_one_lower_and_one_upper_bound()
    {
        Where.Integer("stock").Gt(0).ToString().ShouldBe("""{"integer":{"property":"stock","gt":0}}""");
        Where.Integer("stock").Gt(0).Lte(100).ToString().ShouldBe("""{"integer":{"property":"stock","gt":0,"lte":100}}""");
        Where.Integer("stock").Lt(100).Gte(1).ToString().ShouldBe("""{"integer":{"property":"stock","gte":1,"lt":100}}""");
        Where.Timestamp("launch").Gte(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.FromHours(2))).ToString()
            .ShouldBe("""{"timestamp":{"property":"launch","gte":"2026-06-01T00:00:00.0000000Z"}}""");
    }

    [Fact]
    public void An_empty_range_fails_at_construction()
    {
        Should.Throw<ArgumentException>(() => Where.Integer("stock").Gt(5).Lte(5));
        Should.Throw<ArgumentException>(() => Where.Integer("stock").Gte(5).Lt(5));
        Where.Integer("stock").Gte(5).Lte(5).ShouldNotBeNull();
    }

    [Fact]
    public void Operators_build_flat_groups()
    {
        var where = Where.Text("brand").Eq("Acme") & Where.Integer("stock").Gt(0) & !Where.InSegment("campaigns", "clearance");

        where.ToString().ShouldBe(
            """{"and":[{"text":{"property":"brand","eq":"Acme"}},{"integer":{"property":"stock","gt":0}},{"not":{"segment":{"definitionId":"campaigns","id":"clearance"}}}]}""");
        (Where.HasValue("a") | Where.HasValue("b")).ToString()
            .ShouldBe("""{"or":[{"hasValue":{"property":"a"}},{"hasValue":{"property":"b"}}]}""");
    }

    [Fact]
    public void A_relation_condition_nests_its_where_and_count()
    {
        Where.Related("productVariants").Matching(Where.Integer("stock").Gt(0)).Counting(CountRange.AtLeast(1)).ToString()
            .ShouldBe("""{"relation":{"id":"productVariants","where":{"integer":{"property":"stock","gt":0}},"count":{"gte":1}}}""");
    }

    [Fact]
    public void A_stored_condition_reads_back_and_round_trips_unchanged()
    {
        const string stored = """{"and":[{"text":{"property":"brand","eq":"Acme","ignoreCase":true}},{"lifecycle":{"id":"6f2c25c1-0000-4000-8000-000000000001","state":"Ready"}},{"relation":{"id":"variants","count":{"gte":2}}}]}""";

        Where.Read(JsonDocument.Parse(stored).RootElement).ToString().ShouldBe(stored);
    }

    [Fact]
    public void Raw_sends_an_object_verbatim_and_rejects_anything_else()
    {
        Where.Raw("""{ "lifecycle": { "id": "6f2c25c1-0000-4000-8000-000000000001", "state": "Ready" } }""").ToString()
            .ShouldBe("""{"lifecycle":{"id":"6f2c25c1-0000-4000-8000-000000000001","state":"Ready"}}""");
        Should.Throw<ArgumentException>(() => Where.Raw("[1]"));
    }

    [Fact]
    public void Empty_groups_and_value_lists_fail_at_construction()
    {
        Should.Throw<ArgumentException>(() => Where.All());
        Should.Throw<ArgumentException>(() => Where.Text("brand").In());
        Should.Throw<ArgumentException>(() => Where.HasValue("title", LanguageCode.From("en"), LanguageCode.From("en")));
        Should.Throw<ArgumentException>(() => Where.Integer("stock").In());
    }

    [Fact]
    public void A_card_property_id_starts_with_a_lowercase_letter_then_letters_and_digits()
    {
        CardPropertyId.TryFrom("parentSku").IsSuccess.ShouldBeTrue();
        CardPropertyId.TryFrom("id").IsSuccess.ShouldBeTrue();
        CardPropertyId.TryFrom("Sku").IsSuccess.ShouldBeFalse();
        CardPropertyId.TryFrom("parent_sku").IsSuccess.ShouldBeFalse();
    }
}
