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
