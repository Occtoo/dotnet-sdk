# Documentation

Documentation for people working *on* the SDK. Documentation for people *using*
Occtoo lives at [docs.occtoo.com](https://docs.occtoo.com).

| Document | Contents |
|---|---|
| [authentication.md](authentication.md) | Every way to authenticate, which to choose, and what the SDK handles for you |
| [sources.md](sources.md) | Typed ingest: the entry model, value objects, and the receipt |
| [events.md](events.md) | Typed events: pattern matching, the filter builder, cursors, and the SSE stream |
| [errors.md](errors.md) | The `Result`/`OcctooError` model and how to branch on it |
| [observability.md](observability.md) | Logging categories and levels, and OpenTelemetry tracing |
| [conventions.md](conventions.md) | How the repository is organised and why — layout, targets, packaging, CI, dependency policy |
| [design-principles.md](design-principles.md) | What "good developer experience" means here, and the open design decisions |
| [releasing.md](releasing.md) | How a version is decided, tagged, released and published |

As the remaining feature surfaces land, their guides belong here too, each with
a runnable counterpart under [`../examples/`](../examples).
