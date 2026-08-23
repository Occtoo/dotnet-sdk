<!--
The PR title must follow Conventional Commits — it becomes the squash-merge
commit message and therefore the changelog entry.

  <type>[(scope)][!]: <description>

  feat(ingest): add media upload
  fix(events): keep the cursor when a page is empty
  docs: document the release process
  feat(events)!: rename PullOptions.Limit to PageSize

Types: feat, fix, perf, refactor, docs, test, build, ci, chore, style, revert, deps
-->

## What changed

<!-- One or two sentences. What does this PR do, and why now? -->

## How it was verified

<!-- Tests added or run, manual verification, sample output. -->

## Notes for reviewers

<!-- Trade-offs, follow-ups, anything you want a second opinion on. Delete if none. -->

---

- [ ] Public API changes are documented with XML doc comments
- [ ] `docs/` updated if behaviour or conventions changed
- [ ] Breaking changes are marked with `!` in the title and explained above
