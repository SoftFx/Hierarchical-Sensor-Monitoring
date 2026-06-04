# docs-lint

Lightweight documentation checks for HSM.

## Checks

| Script | Purpose |
|---|---|
| `check-links.sh` | Checks local markdown links under `AGENTS.md`, `aicontext/`, `docs/`, and `.github/`. |
| `check-frontmatter.sh` | Verifies canonical docs have `> Owner: ... | Last reviewed: YYYY-MM-DD | Canonical: yes`. |
| `check-feature-folders.sh` | Verifies feature folders are listed in their area `overview.md` and contain `feature.md`. |
| `check-deprecated-terms.sh` | Flags curated deprecated terms from `aicontext/glossary.md`. |
| `check-pr-format.sh` | Validates a generic PR body from `PR_TITLE` and `PR_BODY` env variables. |

## Local Usage

From repository root:

```bash
bash scripts/docs-lint/check-links.sh
bash scripts/docs-lint/check-frontmatter.sh
bash scripts/docs-lint/check-feature-folders.sh
bash scripts/docs-lint/check-deprecated-terms.sh
PR_TITLE="Refactor collector scheduler" PR_BODY=$'## Summary\nDone.\n\n## What Changed\n- Updated code.\n\n## Tests\n- Passed.\n\n## Risks / Follow-Up\n- None.' bash scripts/docs-lint/check-pr-format.sh
```

Each script exits `0` on success and `1` when it finds problems.

## Maintenance

- Add new canonical docs to `check-frontmatter.sh`.
- Add new feature areas to `check-feature-folders.sh`.
- Add deprecated terminology rules to `check-deprecated-terms.sh` when `glossary.md` grows.
- Keep checks structural. Behavioral correctness belongs in code review and tests.
- Public wiki drafts may use GitHub wiki-style extensionless links; check them separately before publishing.
