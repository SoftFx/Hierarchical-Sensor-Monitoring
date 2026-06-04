# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

HSM (Hierarchical-Sensor-Monitoring) is a .NET 8.0 monitoring platform by Soft-FX. It collects sensor data via HTTP, stores it in LevelDB, and provides a web UI with charts, tables, and Telegram bot alerts. The repo also contains the DataCollector client library (NuGet, multi-targets net6.0/net472), a C++ wrapper, and an independent PingModule worker service.

## Build & Run

**Prerequisites:** .NET 8.0 SDK, Node.js 20.

**Build the main server solution:**
```bash
dotnet build src/server/HSMServer/HSMServer.sln
```
The HSMServer.csproj auto-runs `npm run build_prod` after .NET build via MSBuild targets, producing the webpack bundle. For frontend dev, use `npm run build_dev` (or `npm run watch` with the Development launch profile).

**Build the DataCollector solution:**
```bash
dotnet build src/collector/HSMDataCollector.sln
```

**Run the server:**
```bash
dotnet run --project src/server/HSMServer
```
Ports: **44330** (sensor data API), **44333** (web UI + management API). Requires HTTPS certificates configured in `Config/appsettings.Development.json`.

**Docker:**
```bash
docker-compose up
```

## Tests

**Server core unit tests (xUnit):**
```bash
dotnet test src/tests/HSMServer.Core.Tests/
```

**Database tests (xUnit):**
```bash
dotnet test src/tests/HSMDatabase.LevelDB.Tests/
```

**DataCollector tests (xUnit on net48):**
```bash
dotnet test src/collector/HSMDataCollector.Tests/
```

**Run a single test by name:**
```bash
dotnet test src/tests/HSMServer.Core.Tests/ --filter "FullyQualifiedName~TestClass.TestMethod"
```

**E2E tests (Playwright, TypeScript):** Located in `src/tests/Autotests/`. Run against a live Docker container per the `.github/workflows/tests.yml` workflow.

## Architecture

```
DataCollector (NuGet) / C++ Wrapper
        │  HTTP (port 44330)
        ▼
   HSMServer (ASP.NET Core 8.0 MVC)
   ├── Controllers (sensor API, MVC pages, Swagger)
   ├── HSMServer.Core (business logic: alerts, caching, managers, snapshots)
   ├── HSMCommon (shared utilities, constants, extensions)
   ├── HSMDatabase → HSMDatabase.LevelDB (LevelDB/LMDB + MemoryPack serialization)
   ├── Frontend (TypeScript/webpack: Plotly.js, Bootstrap 5, jsTree, CodeMirror 6, Redux Toolkit)
   └── Telegram bot notifications
```

**Key structural points:**
- The main solution is `src/server/HSMServer/HSMServer.sln` — it includes server core, database layers, tests, benchmarks, and the ping module in solution folders.
- `HSMServer.Core` is the business logic layer (managers, models, services, caching, alerts). Controllers in `HSMServer` are thin, delegating to Core.
- `HSMSensorDataObjects` (netstandard2.0) defines the shared DTOs used by both the server and DataCollector clients.
- `HSMDatabase.AccessManager` abstracts database access; `HSMDatabase.LevelDB` implements it with LightningDB bindings and MemoryPack serialization.
- The frontend source lives in `src/server/HSMServer/wwwroot/src/ts/` and compiles to `wwwroot/dist/` via webpack.
- The `src/module/HSMPingModule/` is an independent .NET Worker Service, built as its own Docker container.

## CI/CD

GitHub Actions workflows (`.github/workflows/`):
- `server-build.yml` — builds, tests, publishes server; creates GitHub Release with zip; pushes Docker image to DockerHub
- `tests.yml` — E2E Playwright tests against a Docker container
- `collector-nuget-build.yml` / `hsmobjects-nuget-build.yml` — pack and push NuGet packages (manual trigger)
- `claude-review.yml` / `claude-agent.yml` — automated Claude Code review and interactive agent on PRs

## Conventions

- C# server code follows ASP.NET Core MVC patterns with cookie-based authentication.
- Frontend uses TypeScript with webpack bundling; no SPA framework — jQuery + Bootstrap 5 with Redux Toolkit for state.
- Database operations go through the AccessManager abstraction layer, not directly to LevelDB.
- NuGet packages (DataCollector, SensorDataObjects) have their own versioning independent of the server.
