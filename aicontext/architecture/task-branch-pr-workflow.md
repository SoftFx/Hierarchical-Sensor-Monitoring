# Task Branch And PR Workflow

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Generic branch and pull request workflow for HSM.

## Branches

- Prefer descriptive task branches.
- Keep one branch focused on one initiative, issue, or coherent change set.
- Do not mix unrelated refactors with feature or bugfix work.

## Pull Requests

Recommended PR body sections:

```md
## Summary

## What Changed

## Tests

## Review Result

## Risks / Follow-Up
```

Use `Review Result` for review roles run, findings fixed, residual risks, and
focused re-review status.

## Review/Fix Loop

- Run role-based review when the PR touches compatibility-sensitive or shared behavior.
- Fix confirmed blockers.
- Rerun only focused roles after fixes.
- Update PR comments/body with what changed and what was verified.

## Handoff

Before handoff:

- worktree is clean or remaining changes are clearly listed;
- targeted tests have passed or failures are explained;
- docs are updated for behavior changes;
- residual risks are explicit;
- remote checks are reported when available.
