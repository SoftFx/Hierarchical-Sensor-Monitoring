# Common Review Rules

Every reviewer must:

- return findings first, ordered by severity `P0` to `P3`;
- include concrete file and line references where possible;
- separate confirmed findings from residual risks and open questions;
- focus on their role instead of duplicating every other reviewer scope;
- avoid changing files unless explicitly asked;
- call out missing tests only when the missing coverage protects a real changed behavior or credible regression;
- identify the affected audience: server operator, HSM site user, collector library integrator, wrapper consumer, or internal worker/maintainer.

## Severity Guide

- `P0`: data loss, security bypass, production outage, corrupt storage, impossible-to-merge public contract break.
- `P1`: likely user-visible bug, broken ingestion/alerting/storage workflow, collector deadlock/leak, migration or compatibility failure.
- `P2`: important edge case, race, missing regression coverage, operational ambiguity, performance risk, contract ambiguity.
- `P3`: polish, naming clarity, minor documentation or test improvement.

## HSM-Specific Baselines

- Preserve backwards compatibility for public collector APIs, DTOs in `HSMSensorDataObjects`, and C++ wrapper headers unless the PR explicitly owns a breaking change.
- Treat sensor ingestion, alerting, history reads, and collector lifecycle as core workflows.
- Be cautious with time-based behavior: timers, scheduler loops, polling, retention windows, alert schedules, retry backoff, and soak tests are common regression sources.
- Prefer deterministic tests over wall-clock sleeps when reviewing new concurrency or timing code.
- Any change that may affect long-running services must be reviewed for disposal, cancellation, bounded queues, memory retention, and diagnostics.

## Output Template

Use this shape unless a parent orchestration asks for another format:

```md
## Findings

- [P1] Title - path/to/file.cs:123
  Explanation, execution path, and why it matters.

## Residual Risks / Open Questions

- Risk or question, if any.

## Tests Reviewed

- Tests run or inspected, plus gaps.
```
