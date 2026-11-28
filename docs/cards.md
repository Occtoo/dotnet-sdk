# Cards

Card definitions shape the entries of one or more sources into cards. The card
features share the identifiers and the condition language on this page.

## Identifiers

Card definition, card, segment and segment definition ids are the
`CardDefinitionId`, `CardId`, `SegmentId` and `SegmentDefinitionId` the Events
feature already uses (`Occtoo.Events`). Two more live in `Occtoo.Cards`:

| Value object | Rule |
|---|---|
| `CardPropertyId` | A lowercase ASCII letter, then letters and digits (`parentSku`); `id` names the card's own id. Case-sensitive, unlike a source `PropertyId` |
| `RelationDefinitionId` | The same rule (`productVariants`) |

Ids Occtoo returns are kept as stored.

## Conditions: `Where`

Segments, card exports and card entry search select entries with a `Where`.
Each leaf names a card property and allows exactly one operation, so a
condition the API would reject for having none or several doesn't compile.

```csharp
using Occtoo.Cards;

var where = Where.Text("brand").IgnoringCase().Eq("Acme")
    & Where.Integer("stock").Gt(0).Lte(100)
    & Where.Related("productVariants").Matching(Where.Boolean("active").Eq(true)).Counting(CountRange.AtLeast(1))
    & !Where.InSegment("campaigns", "clearance");
```

| Leaf | Operations | Modifiers |
|---|---|---|
| `Where.Text(property)` | `Eq`, `Neq`, `In`, `Contains`, `Matches` (whole-value `*` / `?` pattern), `Regex` (RE2 substring), `Length(CountRange)` | `IgnoringCase()`, `InLanguages(…)` |
| `Where.Integer` / `Decimal` / `Timestamp(property)` | `Eq`, `Neq`, `In`, or a range: `Gt`/`Gte` then optionally `Lt`/`Lte`, or the other way round | |
| `Where.Boolean(property)` | `Eq`, `Neq` | |
| `Where.List(property)` | `Contains` (an element), `Matches` (any element), `Only` (every element), `Length(CountRange)` | `IgnoringCase()`, `InLanguages(…)` |
| `Where.HasValue(property, languages…)` | — empty text, whitespace and an empty list count as values | |
| `Where.InSegment(definitionId, segmentId)` | — | |
| `Where.Related(relationId)` | `Matching(where)`, `Counting(CountRange)` — every nested condition applies to the same related card | |

`&`, `|` and `!` (or `Where.All`, `Where.Any`, `Where.Not`) combine
conditions; chains of the same operator stay one flat group. `CountRange` is
`Exactly`, `AtLeast`, `AtMost` or `Between`, all inclusive. With languages,
every listed translation must match: there is no fallback.

Mistakes the API would reject fail when the condition is built, with an
`ArgumentException`: an empty group, an empty `In`/`Only`, duplicate
languages, or bounds that describe an empty range (`Gt(5).Lte(5)`). The
API's size limits — 24 levels, 256 nodes — are checked by Occtoo.

`Where.Raw(json)` sends a condition in the API's JSON form as it is, for what
the builder doesn't model yet, such as lifecycle readiness. A condition read
back from Occtoo, such as a segment's, keeps any branch the SDK doesn't model
verbatim, so it round-trips unchanged. `ToString()` returns the JSON.

## Card definitions

`client.CardDefinitions` manages card definitions (`/v1/card-definitions`):
reads need `read:cards`, writes `write:cards`. A new card definition is a
draft; its card properties wait as pending changes until it is activated.

```csharp
await client.CardDefinitions.Create(
        CreateCardDefinition.WithId("products", "Products")
            .DisplayedAs(new CardDisplay(CardPropertyId.From("title"), default, default))
            .WithProperty(CardProperty.Of("title", "products", "title", SourcePropertyType.LocalizedText)
                .Named(LanguageCode.From("en"), "Title"))
            .WithProperty(CardProperty.Of("tags", "products", "tags", SourcePropertyType.List)
                .WithDelimiter(Delimiter.From(","))))
    .Bind(draft => client.CardDefinitions.Activate(draft.Id, draft.ETag));
```

A card property reads one source property and exposes its type, which must
match the source property's (`SourcePropertyType`) and can't change once the
card property is active. `Named` and `Described` set translations, `Hidden`
and `Editable` the Studio flags, `At` its position, and `FilledFrom` a managed
tag list that fills it on enrichment.

`Activate` applies the pending changes. The card definition comes back
`Updating` while its entries are rebuilt; read it until it is `Active`.
Nothing pending is a `ValidationError`.

### Changes and versions

`Update`, `Activate` and `Delete` carry the version (`ETag`) they were based
on, sent as `If-Match`. When the card definition changed in the meantime the
call is a `ConflictError`: read it again and retry.

```csharp
await client.CardDefinitions.Get("products")
    .Bind(definition => client.CardDefinitions.Update(definition.Edit()
        .WithName("Catalogue")
        .SetProperty(CardProperty.Of("price", "products", "price", SourcePropertyType.Decimal))
        .RemoveProperty("legacyCode")));
```

`Edit()` starts from the version read, including the pending property changes
already saved: the API replaces pending changes as a whole, so `SetProperty`
and `RemoveProperty` add to them, and `DiscardPendingChanges` clears them. A
change to only the display fields of an active card property (name,
description, settings, order, the hidden and editable flags, the managed tag
list) applies at once; any other property change waits for activation. A change
that touches no property sends no pending changes.

`Delete` removes the card definition for good, and its id is free again. One
used by destination endpoints, relation definitions or segments is a
`ConflictError`.

### Exports

`StartExport` writes entries to a CSV or Excel file in the background and
returns the export at once; `GetExport` reads it until its `Status` is final.
Once `Completed`, `File.Url` is a signed download link, made new on every read
and valid for up to 24 hours.

```csharp
var export = await client.CardDefinitions.StartExport("products",
    StartCardExport.As(CardExportFormat.Csv)
        .Matching(Where.Integer("stock").Gt(0))
        .WithProperties("title", "price")
        .InLanguages(LanguageCode.From("en"), LanguageCode.From("sv-SE")));
```

Without `Matching` every entry is exported; without `WithProperties`, every
visible card property; without `InLanguages`, the tenant's default language.
Fewer than 500,000 entries may match. Exports need only `read:cards`.

## Segments

`client.Segments` manages the segments of a card definition
(`/v1/card-definitions/{cardId}/segments`): reads need `read:segments`, writes
`write:segments`. A segment's entries are tagged in the background; it is
`Initializing` until that finishes.

```csharp
await client.Segments.Create("products",
    CreateSegment.Query("summer26", "Summer 26", LanguageCode.From("en"),
            Where.Text("season").Eq("summer") & Where.Integer("stock").Gte(1))
        .ActiveUntil(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

await client.Segments.Create("products",
    CreateSegment.Fixed("featured", "Featured", LanguageCode.From("en"), "chair-1", "table-2"));
```

A `Query` segment keeps following its condition; a `Fixed` one holds the
listed cards. The language is the one localized card properties in the
condition are read in. `ActiveFrom` and `ActiveUntil` take times on the hour;
a start time must be in the future.

A segment evaluates part of the `Where` language: `And` at the top level, `Or`
and `Not` around a single condition; text `Eq`, `In` and `Matches`; list
`Contains` and `Matches`; integer, decimal and timestamp `Eq`, `In`, `Gte` and
`Lte`; boolean `Eq`; `HasValue`; and `Related` by id. Patterns are
case-sensitive, and `HasValue` doesn't count an empty text or list. Anything
else is a `ValidationError` naming the limit.

`Segment.Where` is the stored condition: it can differ in form from what was
sent, never in meaning, and branches the SDK doesn't model are kept as they
are. `Update(segment.Edit().WithWhere(…))` changes only what you set; a new
condition or language starts tagging again. `Delete` archives the segment, and
an archived segment is no longer listed or read.

## Relation definitions

`client.RelationDefinitions` links the cards of a source and a target card
definition (`/v1/relation-definitions`, `read:cards` / `write:cards`). A source
card is related to the target cards whose matched card property holds its own
value. Segments (`Where.Related`) and destination endpoints follow relation
definitions by id.

```csharp
await client.RelationDefinitions.Create(
    CreateRelationDefinition.WithId("productVariants", "Product variants")
        .From("products", "sku")
        .To("variants", "parentSku")
        .Labelled("Variants", "Variant of")
        .OrderedBy(RelationOrder.ByTargetProperty("rank")));
```

Both card definitions must be `Active` and different; the relation id can't
match a card property id of either. The matched properties must be indexed
`Text` or `List` card properties, not both lists, or `id` for the card id. The
links are built in the background: the relation definition is `Initializing`,
then `Active`.

`RelationOrder.ByTargetProperty` orders the targets by a `Decimal` or `Integer`
card property of the target card definition; `RelationOrder.BySourceList`
orders them as the values of a `List` card property on the source card.

`Update(relation.Edit().WithMatch(…))` rebuilds the links (`Updating`, then
`Active`), and is a `ConflictError` while the relation definition isn't
`Active`. `Delete` removes it for good; one a segment or destination endpoint
uses is a `ConflictError`.

## Searching card entries

`client.CardEntries` searches the indexed entries of a card definition
(`/v1/card-definitions/{cardId}/entries/search`, `read:cards`). Values come
back as the same `PropertyValue` union ingest writes and entry reads return.

```csharp
var result = await client.CardEntries.Search("products",
    CardSearch.Matching(Where.Integer("stock").Gt(0), LanguageCode.From("en"))
        .Select("title", "price")
        .OrderBy("price", SortDirection.Descending)
        .FacetOn(CardFacet.On("brand").SortedBy(FacetOrder.Count).Take(10))
        .Take(50));
```

- **Selection:** without `Select` each entry carries only its id and timestamps. A localized property yields one value per language.
- **Paging:** `Take` (1–100, default 25) and `ContinueAfter(page.Next)` page forward; `WithTotal` counts every match. An activation of the card definition invalidates cursors.
- **Facets:** `FacetOn(CardFacet.On(property))` returns the distinct values among the matches with their counts; `Containing`, `SortedBy`, `Take`, `Excluding` and `ContinueAfter` narrow and page them. `FacetsOnly` skips the entries.
- **Segments:** `WithSegments` adds the segments each entry is a member of.
- **Limits:** up to 100 properties, 10 sort keys and 10 facets; a response over 8 MiB is refused.

`SearchRelated(cardDefinitionId, entryId, relationDefinitionId, search)` runs
the same search over the cards related to one card entry. A search changes
nothing, so the SDK resends it after a transient failure like any read.
Lifecycle readiness is not modelled yet.
