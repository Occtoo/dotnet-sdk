# Managed tags: controlled vocabularies

`client.ManagedTags` manages the tenant's managed tags — named, optionally
hierarchical lists of values that cards reference — and their values:
`/v1/managed-tags` and `/v1/managed-tags/{managedTagId}/values`.

```csharp
await client.ManagedTags.Create(new CreateManagedTag("Colors", ManagedTagType.LocalizedText))
    .Bind(colors => client.ManagedTags.CreateValue(colors.Id, new CreateManagedTagValue("blue",
        ManagedTagValueContent.Localized(new Dictionary<string, string> { ["en"] = "Blue", ["sv"] = "Blå" }))
    {
        Order = 1,
    }));
```

Reads require `read:cards`, writes `write:cards` — the tenant-wide
capabilities that also cover cards and card definitions. See
[authentication.md](authentication.md).

## Tags

A `ManagedTag` has a `DisplayName` (unique within the tenant), an immutable
`ManagedTagType` — `Text` or `LocalizedText`, deciding the shape of every
value — and an optional `ParentId` nesting it under another tag. `Update`
replaces the name and parent; changing the parent clears the parent links of
the tag's values. `Delete` removes the tag and its values and detaches nested
tags, but is a `ConflictError` while a card definition still references it.

## Values

A `ManagedTagValue` is keyed by a `ManagedTagValueKey` — one URL path
segment, so no slashes, backslashes, control characters, or dot segments;
the value object enforces this at construction. Its `Content` matches the
tag's type:

```csharp
ManagedTagValueContent.Text("Small")                    // for Text tags — a string literal converts implicitly
ManagedTagValueContent.Localized(new Dictionary<string, string> { ["en"] = "Blue" })  // for LocalizedText tags
```

`Order` is display metadata only — lists are paginated by key — and
`ParentKey` nests a value under another value of the same tag. Keys are
immutable; `UpdateValue` replaces content, order, and parent. Creating a key
that exists is a `ConflictError`; deleting a value clears the parent links
pointing at it.

## Listing

Tag lists filter by name substring, `Type`, `ParentId`, and inclusive
created/updated windows; value lists by key substring (case-sensitive) and
exact `ParentKey`. Both page forward like every management surface — see
[applications.md](applications.md#listing) for cursors and
`OcctooListException`.
