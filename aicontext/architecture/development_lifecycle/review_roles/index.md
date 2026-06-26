# HSM Review Roles Index

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Catalog of reusable subagent role prompts for Hierarchical-Sensor-Monitoring
technical reviews.

## Purpose

Use these roles when launching focused review subagents for PRs that touch the
HSM server, collector SDK, storage layer, UI, docs, tests, wrappers, or
operational behavior.

## Shared Rules

- Shared reviewer rules, severity definitions, and output format: [common.md](common.md)

## Role Index

- Collector SDK lifecycle, sensors, queues, scheduling, public API compatibility: [collector.md](collector.md)
- Server/API behavior, auth, alerts, notifications, background services: [server.md](server.md)
- LevelDB/LMDB storage, snapshots, journals, serialization, retention: [database.md](database.md)
- Operator web UI, dashboards, alerts screens, rendered workflow review: [ux.md](ux.md)
- Test coverage, stress/soak reliability, Playwright/API/integration automation: [testing.md](testing.md)
- Logs, diagnostics, configuration, Docker, deployment, runtime supportability: [observability.md](observability.md)
- Throughput, memory, concurrency, queue pressure, UI/API scalability: [performance.md](performance.md)
- Wiki, API contracts, integrator docs, release notes, examples: [docs.md](docs.md)
- C++ wrapper, sandbox apps, ping module, external integration compatibility: [integrations.md](integrations.md)

## Suggested Review Packs

- Collector SDK PR: `common`, `collector`, `testing`, `performance`, `docs`; add `integrations` for public API/wrapper changes.
- Server/API PR: `common`, `server`, `database`, `testing`, `observability`; add `ux` for visible site changes.
- Storage PR: `common`, `database`, `performance`, `testing`, `observability`.
- UI/alerts PR: `common`, `ux`, `server`, `testing`, `docs`.
- Docs-only PR: `common`, `docs`; add domain role when docs describe changed behavior.
