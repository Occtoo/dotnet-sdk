# Applications: identities and grants

`client.Applications` manages the tenant's machine-to-machine applications —
the identities behind `OcctooCredential.ClientCredentials` — and what each is
allowed to do: `/v1/applications` and `/v1/applications/access-catalog`.

```csharp
var created = await client.Applications.Create(new CreateApplication("Catalog reader")
{
    Description = "Reads the product source configuration",
    ScopeKeys = [OcctooScopes.ReadSources],
    ResourceSelectors = ["source:products"],
});

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
    .Bind(current => client.Applications.Update(id, new UpdateApplication(current.Name, current.Etag)
    {
        Description = current.Description,
        Tags = current.Tags,
        ScopeKeys = [.. current.ScopeKeys, OcctooScopes.ReadEvents],
        ResourceSelectors = current.ResourceSelectors,
        ApiSelectors = current.ApiSelectors,
    }));
```

`Delete` revokes the credentials; deleting an application that no longer
exists succeeds.

## Listing

`List` takes an `ApplicationListQuery` — name substring, tags (all must
match), inclusive created/updated windows, creating/updating actor — and a
`PageRequest`. Lists are forward-only: a `ForwardPage<T>` carries `Items` and
an `After` cursor that is absent on the last page. Keep the same filters when
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

    if (page.Value.After.HasNoValue)
        break;

    query = query with { Page = query.Page with { After = page.Value.After } };
}
```
