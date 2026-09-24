# Managed tags: controlled vocabularies

`client.ManagedTags` manages the tenant's managed tags — named, optionally
hierarchical lists of values that cards reference — and their values:
`/v1/managed-tags` and `/v1/managed-tags/{managedTagId}/values`.

```csharp
await client.ManagedTags.Create(new CreateManagedTag("Colors", ManagedTagType.LocalizedText))
    .Bind(colors => client.ManagedTags.CreateValue(colors.Id, new CreateManagedTagValue("blue",
        ManagedTagValueContent.Localized(new Dictionary<LanguageCode, string> { ["en"] = "Blue", ["sv"] = "Blå" }))
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
replaces the name *and* parent, so an absent `ParentId` un-nests the tag;
start from `tag.Edit()`, which pre-fills both, and change what you mean:

```csharp
await client.ManagedTags.Update(tag.Id, tag.Edit() with { DisplayName = "Colours" });
```

Changing the parent clears the parent links of the tag's values. `Delete` removes the tag and its values and detaches nested
tags, but is a `ConflictError` while a card definition still references it.

## Values

A `ManagedTagValue` is keyed by a `ManagedTagValueKey` — one URL path
segment, so no slashes, backslashes, control characters, or dot segments;
the value object enforces this at construction. Its `Content` matches the
tag's type:

```csharp
ManagedTagValueContent.Text("Small")                    // for Text tags — a string literal converts implicitly
ManagedTagValueContent.Localized(new Dictionary<LanguageCode, string> { ["en"] = "Blue" })  // for LocalizedText tags
```

`Order` is display metadata only — lists are paginated by key — and
`ParentKey` nests a value under another value of the same tag. Keys are
immutable; `UpdateValue` replaces content, order, and parent, so start from
`value.Edit()` — `value.Edit() with { Order = 2 }` — to keep the rest. Creating a key
that exists is a `ConflictError`; deleting a value clears the parent links
pointing at it.

Translations are written keyed by `LanguageCode` and read keyed by the string
the platform stores, so a stored key is never rejected on read. A value in a
shape the SDK cannot hold — a translation that is not a string, say — is a
`DataTypeError` rather than a silent drop. A `Type` this SDK version does not
know reads as absent, like every enum in a response.

## Listing

Tag lists filter by name substring, `Type`, `ParentId`, and inclusive
created/updated windows; value lists by key substring (case-sensitive) and
exact `ParentKey`. Both page forward like every management surface — see
[applications.md](applications.md#listing) for cursors and the paging loop.
