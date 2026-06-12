# Testing Review Roles

## QA / Test Architect

Focus:

- missing positive, negative, boundary, concurrency, and compatibility tests;
- correct layer choice: unit, server core, database, collector, integration, Playwright, stress, soak, or sandbox smoke;
- test reliability for timing, polling, network, filesystem, LevelDB, platform-specific sensors, and background workers;
- whether tests prove documented behavior and bug regressions, not only happy-path execution.

Must read:

- changed production files and adjacent tests;
- `src/collector/HSMDataCollector.Tests`, `src/collector/HSMDataCollector.IntegrationTests`, `src/tests/HSMServer.Core.Tests`, `src/tests/HSMDatabase.LevelDB.Tests`, and `src/tests/Autotests` as relevant;
- docs under `docs/test` for collector stress/soak coverage.

Output:

- untested changed behavior with exact target test file suggestions;
- flaky timing or environment assumptions;
- suggested deterministic synchronization, fixtures, or fakes;
- whether stress/soak coverage is needed or existing tests are enough.

---

## Test Automator

Focus:

- turning review findings into durable automated tests;
- replacing sleeps with signals, `TaskCompletionSource`, controlled clocks, test servers, or deterministic queue drains where possible;
- protecting public API compatibility and storage format behavior;
- making Playwright selectors stable and user-visible.

Must read:

- changed tests and production code enough to choose the right test layer;
- existing helper fixtures and test catalog docs.

Output:

- exact tests to add or update, including proposed names and files;
- notes about fixtures, fake server setup, data seeding, and cleanup;
- residual manual-only gaps.

---

## Manual Smoke Tester

Focus:

- human smoke checks for changed HSM site workflows, collector sample apps, Docker startup, and integration with a running server;
- whether a maintainer can reproduce the expected behavior from docs or sample code.

Must inspect:

- changed UI or sample app in a running environment when feasible;
- relevant README/wiki instructions and screenshots.

Output:

- manual observations with environment and route/app;
- what could not be reached locally;
- recommended smoke-test checklist for the PR.
