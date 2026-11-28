# Destinations

`client.Destinations` manages the APIs that publish cards: destinations, their
versions and the versions' endpoints (`/v1/destinations`). Reads need
`read:destinations`, writes `write:destinations`. Calling a published
destination API with a tenant application is covered in
[authentication.md](authentication.md#protected-destination-apis).

## Creating one, the way Studio does

Studio creates a destination together with its first version, and keeps the
destination's type as a tag. `Create` does the same:

```csharp
using Occtoo.Destinations;

var created = await client.Destinations.Create(
    CreateDestination.Static("webshop", "Webshop feed")     // or Dynamic(…), Enterprise(…)
        .Protected()                                        // the default; Public() needs no credentials
        .WithLanguage(LanguageCode.From("en"))
        .WithLanguage(LanguageCode.From("sv"), LanguageCode.From("sv-SE"))
        .WithDefaultLanguage(LanguageCode.From("en")));

// The audience of tokens for this protected version:
var audience = Audience.From(created.Value.Version.Id.Value);
```

| Type | Studio's description |
|---|---|
| `Static` | A continuous feed to another system: marketplaces, data feeds |
| `Dynamic` | Real-time updates, fetched dynamically |
| `Enterprise` | Large-scale integrations with high availability and security needs |

`Destination.Type` is read from the tags: `Dynamic`, `Multi-consumer`,
`Static`, `Enterprise` or `Partner Zone`. It is absent when none is there.
Multi-consumer destinations can't be created from the SDK yet, because their
first version needs clusters the API doesn't take; Partner Zone destinations
aren't created from Studio either. `Edit().WithTags(…)` keeps the type's tag.

Each `WithLanguage` is a language the API serves, from card data in its
source language (the same one when left out). The first language is the
default unless `WithDefaultLanguage` picks another. Allowed origins default
to any (`*`). A first version without languages is a `ValidationError` before
any request. When the destination is created but its version is refused, the
error comes back and the destination stays without a version; add one with
`CreateVersion`.

Authentication (`Protected` or `Public`) is fixed by the first version.

## Versions

A later version is a copy of the latest one with its endpoints. `CreateVersion`
takes no settings, and needs the latest version to be `Published`.
`UpdateVersion(version.Edit().WithLanguages(…).AllowOrigins(…))` changes the
rest.

`Publish` publishes a new version, or the pending endpoint content of a
published one. The version comes back `Updating`; `GetLatestPublication`
shows how far the publish is and which steps ran. `DeleteVersion` starts
deleting a version and what it published.

## Endpoints

An endpoint returns cards of one card definition. `EndpointContent` picks the
card properties, facets, segments and nested related cards:

```csharp
var content = EndpointContent.Of("products")
    .Property("title", alias: "name")
    .Facet("brand", size: 20)
    .InSegments("summer26")
    .Relation("productVariants", "variants", EndpointCardinality.Plural,
        EndpointContent.Of("variants").Property("sku"),
        new EndpointOrder("rank", SortDirection.Descending));

await client.Destinations.CreateEndpoint("webshop", 1,
    CreateDestinationEndpoint.WithId("products", "Products", content));
```

The endpoint id (3 to 50 letters and digits, not `mcp` or `openapi`) becomes
part of the published URL. Before the version is published a change applies
at once. On a published version, new content waits as `PendingContent` for the
next `Publish`. There, removing a property, relation or facet, changing an
alias, or changing the name or description is a `ConflictError`.
`DiscardPendingContent` drops what waits.

## Versions and conflicts

Changing a destination, a version or an endpoint, and deleting a version,
send the `ETag` of what was read as `If-Match`, through `Edit()` or the
`etag` argument. When the resource changed in the meantime the call is a
`ConflictError`: read it again and retry.
