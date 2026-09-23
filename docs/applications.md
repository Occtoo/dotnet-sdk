# Applications: identities and grants

`client.Applications` manages the tenant's machine-to-machine applications —
the identities behind `OcctooCredential.ClientCredentials` — and what each is
allowed to do: `/v1/applications` and `/v1/applications/access-catalog`.

```csharp
var created = await client.Applications.Create(CreateApplication.Named("Catalog reader")
    .WithDescription("Reads the product source configuration")
    .WithScopes(OcctooScopes.ReadSources)
    .WithSources("products"));

created.Tap(credentials =>
{
    // The client secret is returned exactly once. Store it now.
    vault.Store(credentials.Application.ClientId, credentials.ClientSecret);
});
```

Reads require `read:applications`; writes require `write:applications` — an
administrative capability, since it mints identities and changes their grants.
See [authentication.md](authentication.md).

## The model

`Application` carries the metadata (`Name`, `Description`, `Tags`), the
identity (`ClientId`, `Audiences`), the grants (`ScopeKeys`,
`ResourceSelectors`, `ApiSelectors`), audit timestamps, and an `Etag`. Read
responses never include the secret; only `Create` returns
`ApplicationCredentials` with the `ClientSecret`.

## Building grants

`CreateApplication.Named(...)` (or `application.Edit()` for changes) returns an
`ApplicationBuilder` that spells every grant the way the API expects, so no
one composes selectors by hand:

| Builder method | Grants |
|---|---|
| `WithScopes(OcctooScopes.ReadSources, …)` | Capabilities — use the `OcctooScopes` constants |
| `WithSources("products", …)` | Specific sources |
| `WithAllSources()` | Every current and future source |
| `WithDestinations("webshop", …)` | Every current and future API version of specific destinations |
| `WithAllDestinations()` | Every protected destination API, current and future |
| `WithApiVersions(apiVersionId, …)` | Specific destination API versions |

API versions are granted by id — the id alone identifies a version, whichever
destination it belongs to; the access catalog lists them. The builder drops
duplicates but validates nothing else: the API checks every grant against the
tenant's catalog and answers an unknown one with a `ValidationError`.

`Edit()` starts from the application's current settings and grants, so an
update built from it keeps everything you do not touch. It can only add
grants; to remove one, start from `CreateApplication.Named(...)` with only
the grants to keep and finish with `.BuildUpdate(current.Etag)`.

The valid grants come from the access catalog — a tree of `AccessNode`s
(scopes, resources, destination APIs) — rather than from documentation, so
what a tenant can grant is always what the API accepts:

```csharp
var catalog = await client.Applications.GetAccessCatalog();
```

## Updating with etags

`Update` replaces every setting: name, description, tags, and all three grant
collections — a collection you leave empty *becomes* empty. It requires the
`Etag` from the latest read, and a stale one is rejected with a
`ConflictError`, so a concurrent change can never be silently overwritten:

```csharp
await client.Applications.Get(id)
    .Bind(current => client.Applications.Update(id,
        current.Edit().WithScopes(OcctooScopes.ReadEvents).BuildUpdate(current.Etag)));
```

`Delete` revokes the credentials; deleting an application that no longer
exists succeeds.

## Listing

`List` takes an `ApplicationListQuery` — name substring, tags (all must
match), inclusive created/updated windows, creating/updating actor — and a
`PageRequest`. Lists are forward-only: a `Page<T>` carries `Items`, a `Next` cursor, and
`HasMore` — false on the last page, where management lists return no cursor. Keep the same filters when
following a cursor; it identifies a position in the *filtered* list.

The SDK does not auto-paginate: how far to read, and what to do when a page
fails midway, is the caller's call. Draining a list is a short loop:

```csharp
var query = new ApplicationListQuery();
while (true)
{
    var page = await client.Applications.List(query);
    if (page.IsFailure)
        break; // or retry, or surface — every page is its own Result

    foreach (var application in page.Value.Items)
        Handle(application);

    if (!page.Value.HasMore)
        break;

    query = query with { Page = query.Page with { After = page.Value.Next } };
}
```
