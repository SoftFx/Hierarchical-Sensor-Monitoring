# HSMServer Overview

> Owner: server | Last reviewed: 2026-05-26 | Canonical: yes

## Purpose

HSMServer is the central ASP.NET Core 8.0 MVC application that receives sensor data from DataCollector instances, stores history in LevelDB, and provides a web dashboard for visualization, alerting, and management.

## Architecture

```
Browser (:44333)                 DataCollector (:44330)
    |                                    |
    v                                    v
HSMServer (ASP.NET Core MVC)     API Controllers (/api/sensors/*)
    |                                    |
    +-- Views (Razor + TS/Webpack)       +-- TreeValuesCache
    +-- Controllers (MVC + API)          +-- UpdatesQueue (Channel-based)
    +-- BackgroundServices               +-- BaseSensorModel<T>
    +-- ConcurrentStorage<T>             +-- Policies (TTL, alerts)
    |                                    |
    +------------ LevelDB ---------------+
```

## Key Components

### TreeValuesCache
In-memory tree of Products -> Nodes -> Sensors. Source of truth for current sensor state. Backed by LevelDB for persistence across restarts.

### UpdatesQueue
`System.Threading.Channels`-based async queue for processing incoming sensor value updates. Decouples HTTP request handling from sensor model updates.

### BaseSensorModel<T>
Typed sensor model. Manages:
- Last value and history
- TTL (time-to-live) expiration
- Alert policies and conditions
- Status computation

Initialization is expected before values are accepted. `Initialize()` loads history from LevelDB under a per-sensor lock and publishes its `_isInitialized` flag only once that load has **finished or failed** (#1296), so the three value-ingress gates — `TryAddValue`, `TryUpdateLastValue`, `CheckTimeout` — park on the lock instead of racing past on an empty `Storage`. Two limits the contract does not cover: a failed load latches as well and leaves an empty `Storage` (deliberate — one logged error beats a per-value retry storm against a broken database), and only those three gates wait; direct readers such as `LastValue`, `HasData`, or `Revalidate()` are unguarded and may observe a mid-load sensor.

**Invariant — nothing reached from inside a sensor's initialization lock may block.** This is the canonical statement; the code carries one-line pointers here rather than four copies of the reasoning.

Policy evaluation runs inside that lock and fans out well past a database read: `Policies.TryValidate` → `CalculateStorageResult` → `SensorTimeout` → `SensorExpired` → `TreeValuesCache.SetExpiredSnapshot`, and from there both `SensorUpdateView` → every `ChangeSensorEvent` subscriber (view-model updates in `TreeViewModel`, panel bookkeeping in `Dashboard`) and `SendNotification` → `ConfirmationManager` → alert dispatch. So the lock is held across up to three LevelDB reads plus all of that. None of those paths takes a blocking lock today, and none may start — in particular nothing there may wait on the `UpdatesQueue`, a `SingleReader` channel (`MaxQueueSize` 1000, `FullMode.Wait`) whose reader, parked on a sensor lock, back-pressures all the way to the HTTP ingest threads.

Why startup is nonetheless not serialized: `FillSensorsData` loads sensors one at a time on a single background thread, so at most one queue reader is parked at any moment, and only for that one sensor's load.

Direct `Storage` readers stay unguarded. One of them is a known data-loss path: `ShouldDestroy()` reads `HasData`/`LastUpdate`, and an uninitialized sensor falls back to `CreationDate` and destroys itself with its history — see #1328.

Changes around `TryAddValue`, `TryUpdateLastValue`, TTL, or policy loading should include tests for first-value initialization and timeout behavior.

### ConcurrentStorage<T>
Thread-safe in-memory storage pattern that syncs writes to LevelDB. Used for products, users, access keys, sensor configs.

### Background Services
- Data collection (self-monitoring)
- Database snapshots
- Data cleanup/retention
- Notification delivery (Telegram, email)
- SFTP backups

## Web Frontend

TypeScript 5.3 + Webpack 5:
- jQuery + Bootstrap 5 for UI
- Plotly.js for charts
- DataTables for tables
- Redux Toolkit for state
- jstree for sensor tree navigation
- CodeMirror 6 for code editors

## Authentication

- Cookie-based authentication
- Custom `UserManager` with roles: Viewer (read-only), Manager (read-write)
- Access keys authenticate DataCollector instances (no user session needed)

## Feature Folders To Add Here

- `ingestion/` - receiving and validating collector data.
- `alerts/` - alert conditions, templates, schedules, notification triggers. See `alerts/feature.md` for the canonical model: global alerts via `AlertTemplate` plus per-sensor editor; node-level alerting on Folders/Products was removed in #1142.
- `notifications/` - Telegram/email delivery, retries, failure handling.
- `dashboards/` - server-owned dashboard behavior and data shaping.
- `auth/` - authentication, access keys, users, permissions.
- `background-services/` - hosted services, queue workers, startup/shutdown.

Create folders from `../_TEMPLATE_feature.md` as work lands.
