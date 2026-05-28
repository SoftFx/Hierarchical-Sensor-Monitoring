# Testing Index

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Map of HSM test suites and when to run them.

## Collector

| Suite | Path | Use When |
|---|---|---|
| Collector unit/adversarial/stress tests | `src/collector/HSMDataCollector.Tests/HSMDataCollector.Tests.csproj` | Collector lifecycle, sensors, queues, scheduler, transport, logging |
| Collector integration tests | `src/collector/HSMDataCollector.IntegrationTests/HSMDataCollector.IntegrationTests.csproj` | Collector/server integration and fixtures |

## Server / Core

| Suite | Path | Use When |
|---|---|---|
| Server core tests | `src/tests/HSMServer.Core.Tests/HSMServer.Core.Tests.csproj` | Core model, cache, update queues, monitoring logic |
| HSM Server solution | `src/server/HSMServer/HSMServer.sln` | Server build or broader compile check |

## Storage

| Suite | Path | Use When |
|---|---|---|
| LevelDB tests | `src/tests/HSMDatabase.LevelDB.Tests/HSMDatabase.LevelDB.Tests.csproj` | Storage key formats, journals, sensor values, snapshots |

## UI / E2E

| Suite | Path | Use When |
|---|---|---|
| Playwright autotests | `src/tests/Autotests` | Site workflows, login, registration, environment flows |

## Guidance

- Prefer targeted tests near the changed component first.
- Use deterministic synchronization for timing/concurrency changes.
- Treat skipped soak/platform-gated tests as residual risk when the change affects that area.
- Record exact command and result in PR handoff.
