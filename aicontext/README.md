# aicontext - HSM documentation map

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

`aicontext/` is the canonical agent-facing documentation area for
Hierarchical-Sensor-Monitoring. Keep it close to the code: when behavior changes,
update the relevant feature docs, glossary, and review/process docs in the same
work cycle.

## Start Here

1. `glossary.md` - canonical HSM terms and deprecated wording.
2. `features/` - behavior owned by product areas and components.
3. `architecture/development_lifecycle/review_roles/` - reusable subagent review roles.
4. Existing public docs under `ai-docs/`, `docs/`, `wiki-draft/`, and `README.md` when the change is user- or integrator-visible.

## Directory Structure

| Path | Purpose | Read When |
|---|---|---|
| `features/collector/` | DataCollector SDK, sensors, queues, lifecycle, logging | Changing collector library behavior or public collector APIs |
| `features/server/` | HSM server, background services, alerts, notifications, dashboards | Changing server behavior or operational workflows |
| `features/storage/` | LevelDB/LMDB storage, snapshots, journals, cache persistence | Changing database shape, key format, retention, or history behavior |
| `features/site/` | HSM web site screens and operator workflows | Changing rendered UI or operator-visible behavior |
| `features/api/` | API contracts shared by server, collector, wrappers, and docs | Changing DTOs, routes, request/response semantics, or compatibility |
| `features/integrations/` | C++ wrapper, ping module, sandbox apps, external examples | Changing external integration surfaces |
| `screens/site/` | HSM site screen specs and operator workflow notes | Changing rendered UI or screen-level behavior |
| `flows/` | Mermaid diagrams for non-trivial multi-component flows | Changing async, queue, alerting, storage recovery, or lifecycle flows |
| `architecture/development_lifecycle/` | Review roles and development process docs | Running or changing review/fix loops |

## Feature Documentation Rules

- Create a folder under the closest product area, for example `features/collector/scheduler/`.
- Start from `features/_TEMPLATE_feature.md`.
- Keep `feature.md` focused on behavior and invariants.
- Add `tests.md` for important test scenarios.
- Add storage/API/docs companion files only when that feature owns those contracts.
- Update `glossary.md` before introducing new canonical names.

## Common Reading Paths

Collector SDK change:
`glossary.md` -> `features/collector/overview.md` -> target feature folder -> `review_roles/collector.md`.

Server/API change:
`glossary.md` -> `features/server/overview.md` or `features/api/overview.md` -> target feature folder -> matching review roles.

Storage change:
`glossary.md` -> `features/storage/overview.md` -> target feature folder -> `review_roles/database.md`.

UI/docs change:
`glossary.md` -> `features/site/overview.md` -> `screens/site/` when screen behavior changes -> public docs under `ai-docs/`, `docs/`, or `wiki-draft/`.
