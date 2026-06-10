# HSM — Codex Instructions

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

## Project Overview

HSM (Hierarchical Sensor Monitoring) — система мониторинга иерархических сенсоров.
Клиентские приложения встраивают NuGet-библиотеку **HSMDataCollector**, которая собирает метрики (CPU, RAM, disk, custom) и отправляет их на **HSMServer** по HTTPS.
Сервер хранит историю в LevelDB, визуализирует данные через веб-интерфейс и поддерживает алерты, уведомления (Telegram) и интеграцию с Grafana.

## Tech Stack

| Layer | Technology |
|---|---|
| Server | ASP.NET Core 8.0 MVC + Razor Views |
| DataCollector (client library) | .NET 6.0 / .NET Framework 4.7.2 (NuGet) |
| Shared API DTOs | C# Class Library (`HSMSensorDataObjects`) |
| Database | LevelDB (через LightningDB) |
| Frontend | TypeScript 5.3 + Webpack 5 + jQuery + Bootstrap 5 + Plotly.js |
| Auth | Cookie-based + custom roles (Viewer, Manager) |
| Notifications | Telegram Bot |
| Infrastructure | Docker |

## Repository Structure

```
/
  /src
    /api/HSMSensorDataObjects        -> Shared DTOs между collector и server
    /collector/HSMDataCollector       -> Client NuGet library
    /collector/HSMDataCollector.Tests -> Unit/stress/chaos тесты collector
    /server/HSMCommon                -> Shared utilities
    /server/HSMServer.Core           -> Business logic (cache, models, services)
    /server/HSMServer                -> ASP.NET Core MVC web app
    /database/HSMDatabase*           -> LevelDB data access layer
    /module/HSMPingModule            -> Standalone ping monitoring module
    /sandbox/*                       -> Test/benchmark apps
    /tests/Autotests                 -> Playwright E2E tests (TypeScript)
    /tests/HSMServer.Core.Tests      -> Server unit tests (xUnit)
    /tests/HSMDatabase.LevelDB.Tests -> Database unit tests (xUnit)
  /aicontext                         -> AI-readable canonical documentation
    /architecture                    -> System architecture, testing, docker, dev lifecycle
    /features/collector              -> DataCollector feature docs
    /features/server                 -> Server feature docs
    /features/api                    -> API specs and sensor data contracts
    /features/_TEMPLATE_feature.md   -> Template for adding feature docs
  /wiki-git                          -> GitHub Wiki source
  /wiki-draft                        -> Wiki drafts
```

## Network Ports

```
:44330  -> HSMServer Sensor API (DataCollector sends data here)
:44333  -> HSMServer Web UI (admin/user dashboard)
```

## Architecture Rules — ALWAYS follow these

1. **After behavior changes** -> update relevant docs in `aicontext/` from the actual implementation before review/handoff
2. **DataCollector is a library used by third parties** -> public API changes must be backward-compatible; no breaking changes without major version bump
3. **DataCollector targets net6.0 + net472** -> no APIs unavailable on either TFM; test both TFMs when touching shared collector code
4. **No business logic in controllers** -> logic lives in Services / Core
5. **Thread safety is critical in DataCollector** -> all public sensor methods can be called from any thread; use `Interlocked`, `Volatile`, `lock` correctly
6. **Exception isolation** -> exceptions in sensors, timers, callbacks must never crash the host process; always catch and route to `HandleException`
7. **Resource cleanup** -> every `IDisposable` must be disposed; `HttpResponseMessage`, `HttpContent`, `Process`, `ServiceController` etc.
8. **No silent data loss** -> queue overflows, send failures, dropped values must be logged or reported through diagnostic sensors

## Key Documentation — ALWAYS READ before making changes

- System overview: `aicontext/architecture/overview.md`
- DataCollector internals: `aicontext/features/collector/overview.md`
- Server internals: `aicontext/features/server/overview.md`
- API contracts (sensor data): `aicontext/features/api/overview.md`
- Testing strategy: `aicontext/architecture/testing.md`
- Docker setup: `aicontext/architecture/docker.md`
- Development lifecycle: `aicontext/architecture/development-lifecycle.md`
- Glossary: `aicontext/glossary.md`

## Feature Documentation

Each feature is described in `aicontext/features/{area}/{feature}/`:
- `feature.md` -> behavior description, business rules, invariants
- optional `tests.md` -> test scenarios when a feature needs a separate test matrix

Reading order:
1. Open `feature.md` of the relevant feature to understand product behavior.
2. If present, open `tests.md` next to it for test scenarios.
3. Check `overview.md` of the component for cross-feature context.

When adding new functionality:
- Collector-only -> `aicontext/features/collector/{feature}/`
- Server-only -> `aicontext/features/server/{feature}/`
- Cross-component -> add an explicit area under `aicontext/features/` and link it from `aicontext/README.md`

## Known Invariants

- DataCollector lifecycle: `Stopped -> Starting -> Running -> Stopping -> Stopped`; `Disposed` is terminal
- `Dispose()` must work from any state and must not throw
- `CollectorScheduler` is a per-collector instance (owned and disposed by its `DataCollector`); there is no process-global scheduler
- Sensor values are queued in `Channel<QueueItem<T>>` and sent in batches by `QueueProcessorBase`; overflow/retry policy per `aicontext/features/collector/data-pipeline/feature.md`
- HTTP retries use Polly with exponential backoff; currently no `ShouldHandle` for HTTP status codes (known issue)
- `MessageDeduplicator` bounds cache size to prevent memory leaks from diverse exception messages

## Versioning

- Do not bump unrelated component versions just because code changed.
- DataCollector package version is in `src/collector/HSMDataCollector/HSMDataCollector.csproj`.
- Shared API DTO package version is in `src/api/HSMSensorDataObjects/HSMSensorDataObjects.csproj`.
- Server application version is in `src/server/HSMServer/HSMServer.csproj`.
- Bump the relevant version only when preparing a release/package or when the task explicitly asks for it.

## Code Style Rules — ALWAYS follow

- **No Russian comments in code.** All code comments must be in English.
- Documentation in `aicontext/` can be in Russian or English.
- Do not revert unrelated user changes.
- Treat untracked files as user-owned unless the current task created them or explicitly asks to remove them.
- Keep changes scoped to the request and the touched feature.
- Prefer existing project patterns over new abstractions.
- Run the narrowest meaningful tests first, then broader suites when shared behavior changes.

## Naming Conventions

- Sensors: `{Type}Sensor.cs` (e.g., `MonitoringRateSensor.cs`)
- Options: `{Feature}Options.cs` (e.g., `BarSensorOptions.cs`)
- Tests: `{Feature}Tests.cs` with xUnit `[Fact]` / `[Theory]`
- Commit messages: imperative mood, describe the fix/feature (e.g., "Fix collector lifecycle stop states")

## Documentation Rules

- Update `aicontext/features/...` when behavior or invariants change.
- Update `aicontext/glossary.md` before introducing new canonical terms.
- Add an ADR under `docs/decisions/` for durable architecture decisions.
- Add an initiative under `docs/initiatives/` for multi-PR or ambiguous product/technical work.
- Keep docs product-neutral and HSM-specific; do not copy external project terms.

## Review Rules

- Use `aicontext/architecture/development_lifecycle/technical_review_orchestration.md` for role-based review.
- Initial review may use a role pack selected from changed surfaces.
- Re-review after fixes must be focused: rerun only roles whose risk area changed.
- Findings should lead with severity, file/line references, impact, and suggested fix.
- Never merge to `master` or `main`; stop at human handoff.

## Compatibility Rules

- Public collector APIs, DTOs in `HSMSensorDataObjects`, C++ wrapper headers, and serialized storage formats are compatibility-sensitive.
- Treat scheduler/lifecycle, storage key formats, alert delivery, notification routing, and long-running queues as high-risk areas.
- Document intentional breaking changes explicitly in PR text and public docs.
