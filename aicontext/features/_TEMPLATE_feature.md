# Feature: <Feature Name>

> Owner: <collector | server | storage | site | api | integrations | shared> | Last reviewed: YYYY-MM-DD | Canonical: yes
> Scope: <one sentence explaining what behavior this feature owns>

---

## Overview

<Describe what the feature does and who depends on it. Keep this behavioral, not just code structure.>

## Invariants

- <Rules the implementation must always preserve.>
- <Concurrency, compatibility, persistence, or lifecycle constraints.>
- <Important null/default/time/status semantics.>

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | <Short workflow name> | <operator / collector integrator / server / background worker> |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| <DTO/interface/route> | `<path>` | <compatibility notes> |

## Storage / Persistence

<Describe owned storage, key formats, retention, snapshots, or say "None".>

## UI / Operator Visibility

<Describe where operators can see/debug this behavior, or say "Not operator-visible".>

## Dependencies

- Depends on: <feature/component>
- Used by: <feature/component>

## Tests

Create `tests.md` next to this file when the feature has non-trivial coverage.

Required coverage checklist:

- happy path;
- negative/error path;
- boundary/default behavior;
- concurrency/lifecycle behavior when relevant;
- compatibility/storage behavior when relevant.

## Notes

<Implementation notes, known limitations, follow-ups, or migration notes.>
