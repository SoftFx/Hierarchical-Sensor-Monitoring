# Technical Review Orchestration

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Entrypoint for role-based technical review with subagents.

## Core Rule

The orchestrator chooses roles from the actual diff and risk. Do not use fixed
presets as the source of truth. Initial review can be broad; re-review after
fixes must be focused.

## Read Order

1. [review_roles/common.md](review_roles/common.md)
2. [review_roles/index.md](review_roles/index.md)
3. Only the role files selected for the current pass.

## Orchestrator Output

Before launching reviewers, produce:

- touched surfaces;
- roles to run now;
- roles intentionally skipped and why;
- parallel groups and dependencies;
- for re-review, findings being validated and fix surfaces.

## Surface Classification

- Collector SDK: public collector API, sensors, lifecycle, scheduler, queues, transport, logging.
- Server/API: controllers, DTO conversion, auth/access, validation, background services.
- Storage/cache: LevelDB/LMDB, snapshots, journals, key formats, cache recovery.
- Site/UI: HSM web views, dashboards, alert screens, operator workflows.
- Alerts/notifications: alert conditions, schedules, Telegram/email delivery, deduplication.
- Integrations: C++ wrapper, ping module, sandbox/sample apps.
- Docs/process: aicontext, wiki, README, release notes, workflow docs.
- Tests: collector tests, server core tests, database tests, Playwright, stress/soak.
- Operations: Docker/config, deployment, native dependencies, logging, rollback.

## Initial Review Rules

- Select the smallest useful role pack from [review_roles/index.md](review_roles/index.md).
- Include every role whose surface changed materially.
- Independent roles can run in parallel.
- If impact cannot be bounded, broaden the first pass.

## Re-Review Rules

- Never rerun the full initial pack by default.
- Compare the fix diff against the surface map.
- Rerun the finding author when the fix is non-trivial.
- Add only roles whose surface was changed by the fix.
- Skip subagent re-review when fixes are mechanical and covered by targeted tests.

## Handoff Requirements

Final review/fix handoff should include:

- fixed findings;
- tests run and results;
- unresolved residual risks;
- docs updated or explicitly not needed;
- remote check status when available.
