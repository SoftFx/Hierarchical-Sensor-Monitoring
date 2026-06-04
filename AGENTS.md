# HSM Agent Guide

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

This file defines repository-local expectations for AI agents and maintainers
working in Hierarchical-Sensor-Monitoring.

## Read Order

1. `aicontext/README.md` - documentation map.
2. `aicontext/glossary.md` - canonical terminology.
3. Relevant `aicontext/features/...` docs for the touched behavior.
4. Relevant review roles under `aicontext/architecture/development_lifecycle/review_roles/`.
5. Existing public docs under `README.md`, `docs/`, `ai-docs/`, or `wiki-draft/` when user- or integrator-visible behavior changes.

## Worktree Rules

- Do not revert unrelated user changes.
- Treat untracked files as user-owned unless the current task created them or explicitly asks to remove them.
- Keep changes scoped to the request and the touched feature.
- Prefer existing project patterns over new abstractions.
- Run the narrowest meaningful tests first, then broader suites when shared behavior changes.

## Documentation Rules

- Update `aicontext/features/...` when behavior or invariants change.
- Update `aicontext/glossary.md` before introducing new canonical terms.
- Add an ADR under `docs/decisions/` for durable architecture decisions.
- Add an initiative under `docs/initiatives/` for multi-PR or ambiguous product/technical work.
- Keep docs product-neutral and HSM-specific; do not copy external project terms.

## Review Rules

- Use `aicontext/architecture/development_lifecycle/technical_review_orchestration.md` for role-based review.
- Initial review may use a role pack selected from changed surfaces.
- Re-review after fixes must be focused: rerun only roles whose risk area changed.
- Findings should lead with severity, file/line references, impact, and suggested fix.
- Never merge to `master` or `main`; stop at human handoff.

## Compatibility Rules

- Public collector APIs, DTOs in `HSMSensorDataObjects`, C++ wrapper headers, and serialized storage formats are compatibility-sensitive.
- Treat scheduler/lifecycle, storage key formats, alert delivery, notification routing, and long-running queues as high-risk areas.
- Document intentional breaking changes explicitly in PR text and public docs.
