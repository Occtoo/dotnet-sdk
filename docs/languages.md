# Languages

`client.Languages` reads and replaces the tenant's languages and its default
language (`/v1/languages`). Reads need `read:cards`, writes `write:cards`.

```csharp
using Occtoo.Languages;

await client.Languages.Get()
    .Map(languages => languages
        .With(LanguageCode.From("sv-SE"), "Swedish")
        .WithDefault(LanguageCode.From("sv-SE")))
    .Bind(languages => client.Languages.Replace(languages));
```

`TenantLanguages` holds the `Default` code and the `Items`, each a
`TenantLanguage` with its `Code` and an optional `Name`. A language Occtoo found
in source entries for the first time has no name. `With` adds a language or
renames one the tenant already has, `Without` removes one (and clears the
default if it was that one), and `WithDefault` picks the default.

`Replace` sends the whole list: a language left out is removed. Its data stays,
and the language returns only when a source first receives entries in it. Start
from `Get`, so a language added in the meantime isn't lost. A default that isn't
one of the items is a `ValidationError` before the request.

## Codes

A `LanguageCode` you construct is checked as an ISO code (`en`, `sv-SE`,
`zh-Hans-CN`). A code the tenant already holds comes back exactly as stored,
even when it isn't ISO (`EN_legacy`, say), so it survives a `Get`/`Replace`
round trip unchanged. Occtoo matches codes exactly, case included. The same
applies to the language of a stored entry value and of an event's property
value.
