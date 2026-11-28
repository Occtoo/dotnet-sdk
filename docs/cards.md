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
