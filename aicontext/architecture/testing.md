# Testing Rules

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

## Principles

- Tests should prove behavior and regressions, not only exercise code paths.
- Prefer deterministic signals over wall-clock sleeps for concurrency and timers.
- Keep tests close to the layer where the risk lives.
- Use stress/soak tests for confidence, not as the only regression guard.

## Layer Selection

- Collector lifecycle/scheduler/queue behavior: collector unit or adversarial tests.
- Server domain/cache/update behavior: server core tests.
- Storage key/serialization/history behavior: LevelDB tests.
- Rendered site workflows: Playwright tests.
- Public API/DTO compatibility: serialization or integration tests near the contract.

## Timing And Concurrency

- Use `TaskCompletionSource`, events, fake clocks, bounded waits, or explicit queue drains.
- Avoid tests that assert exact timing on loaded CI runners.
- For disposal/cancellation changes, verify no late callbacks, unobserved exceptions, or retained workers when feasible.

## Handoff

Every review/fix handoff should include:

- commands run;
- pass/fail/skip counts;
- skipped tests relevant to the change;
- any tests not run and why.
