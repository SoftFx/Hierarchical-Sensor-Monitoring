# aicontext — documentation map

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

`aicontext/` — canonical agent documentation for HSM. AI agents and engineers read it before writing code and update it after changes based on the actual diff (see `AGENTS.md` -> Architecture Rules).

## Getting started (new person / new agent)

1. **`../AGENTS.md`** — project rules, architecture constraints, invariants.
2. **`glossary.md`** — canonical terms. If unsure how to name something, start here.
3. **`architecture/overview.md`** — system architecture: components, data flow, deployment.
4. **Component overviews** -> `features/collector/overview.md`, `features/server/overview.md`, `features/api/overview.md`.
5. Specific feature -> its folder in `features/`: `feature.md` (what it does), optional `tests.md` (scenarios).

## Directory structure

| Folder | Contents | When to read |
|---|---|---|
| `features/collector/` | DataCollector features: scheduling, sensors, data pipeline, HTTP client | Working on the NuGet client library |
| `features/server/` | Server features: cache, alerts, notifications, dashboards | Working on HSMServer or HSMServer.Core |
| `features/api/` | API specs: sensor endpoints, data contracts, DTOs | Changing the collector<->server protocol |
| `architecture/` | System docs: overview, testing, docker, database, dev lifecycle | Architectural changes, new processes |
| `features/_TEMPLATE_feature.md` | Template for new feature docs | Adding a new documented feature area |

## Typical reading paths (by task)

**Fixing a DataCollector bug**
`AGENTS.md` -> `features/collector/overview.md` -> specific feature's `feature.md` -> `architecture/testing.md`.

**Changing sensor data contracts**
`AGENTS.md` -> `features/api/overview.md` -> `features/collector/overview.md` -> `features/server/overview.md`.

**Adding a new default sensor**
`AGENTS.md` -> `features/collector/sensors/feature.md` -> `features/collector/scheduling/feature.md` -> `architecture/testing.md`.

**Running/writing tests**
`architecture/testing.md` -> relevant test project.

## Quick FAQ

- **Canonical architecture rules** -> `../AGENTS.md` Architecture Rules.
- **Glossary and canonical terms** -> `glossary.md`.
- **Existing wiki docs (user-facing)** -> `../wiki-git/`.

## Documentation maintenance rules

- After behavior changes -> update relevant `aicontext/*.md` from the actual diff before review/handoff.
- Don't duplicate one rule in multiple places: link to the canonical document.
- Each top-level document has a header `> Owner: ... | Last reviewed: YYYY-MM-DD | Canonical: yes`. Older than 6 months = read with suspicion; update date on changes.
