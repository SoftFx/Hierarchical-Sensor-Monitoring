# Feature: <Feature Name>

> Owner: <collector | server | shared> | Last reviewed: YYYY-MM-DD | Canonical: yes
> Scope: <Collector | Server | Shared> - <one line about why this feature exists>

<!--
Template for feature.md. Copy to `aicontext/features/{area}/{feature}/feature.md`,
fill in sections, delete comments. Optional sections marked [optional] — keep only applicable ones.
-->

---

## Description

<What the feature does. 1-3 paragraphs of plain text. Product-level behavior, not implementation details.>

---

## Business Rules / Invariants

- <Hard invariants that code must enforce.>
- <Edge cases and null semantics.>
- <Thread safety requirements.>

---

## Key Files

| File | Purpose |
|---|---|
| `src/.../FileName.cs` | <What this file does> |

---

## Data Flow [optional]

<How data moves through this feature. Use plain text or a brief Mermaid diagram reference.>

---

## Dependencies [optional]

- Depends on: [`<feature-name>`](../<feature-name>/feature.md) — <why needed>
- Used by: [`<feature-name>`](../<feature-name>/feature.md) — <how used>

---

## Test Scenarios

- <Positive path>
- <Negative path>
- <Concurrency / failure path if relevant>

For large matrices, add a sibling `tests.md` and link it from this section.

---

## Known Issues / Limitations [optional]

<Anything important to keep near feature.md but not part of the product description: migration plans, v1/v2 scope, known limitations.>
