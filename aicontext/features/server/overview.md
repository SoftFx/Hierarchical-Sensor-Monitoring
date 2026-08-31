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

Why startup is nonetheless not serialized: **the lock is per-sensor**, so a waiter only ever blocks behind that one sensor's load. `Initialize()` is driven from several places at once — `FillSensorsData`, `CheckSensorsTimeout` → `CheckTimeout()` on each product's queue reader, `ProductModel.CheckTimeout()` from `BaseNodeModel.TryUpdate`, and the ingest path — so several sensors can be mid-load on several threads, and more than one reader can be parked at a time.

Worst case worth knowing (not new — `master` does the same loads): a product-settings save during startup runs `TryUpdate` → `ProductModel.CheckTimeout()` → a serial history load for every sensor in the product, on the caller's thread, and now also stalls that product's queue reader behind each in turn. Bounded, but it is the latency ceiling this change makes visible.

Direct `Storage` readers stay unguarded, with one now-guarded exception: `ShouldDestroy()` (#1328).

- **What changed:** `ShouldDestroy()` defers the decision (returns `false`) unless `IsHistoryLoaded` is true — meaning "history actually loaded", not "a load was attempted" (a failed load latches `_isInitialized` but never publishes; for such sensors self-destroy is disabled between the bounded retries below, logged at `Warn` by the sweep). With an empty `Storage` cache (retention purge, a full history clear, or the newest row is the `SetExpiredSnapshot` marker, which never enters the cache) it falls back to the newest of the timeout marker's `.Time` and `Storage.To` (the last real value's time — `Clear()` does not reset it), and to `CreationDate` only when the sensor never received a value. Newest-of, not a priority chain: `_lastTimeout` is not advanced once a newer real value arrives (a later re-expiry writes no marker, because `SetExpiredSnapshot` requires `HasData`), so a stale marker would otherwise shadow a fresher `To`. The marker path is deliberately conservative: the marker's time over-estimates last activity by up to one TTL. `ShouldDestroy()` stays a pure predicate: no history load, no policy fan-out, no notifications. The sweep itself is self-sufficient for never-attempted sensors: it calls `Initialize()` on any self-destroy-enabled sensor that `CheckSensorsHistoryAsync` never reached (e.g. sensors lost to a product-name or sensor-path collision), with per-sensor exception isolation.
- **What it relies on:** `ClearDatabaseService.ServiceActionAsync` awaits `CheckSensorsHistoryAsync` (which initializes every sensor reachable through the cache, with per-sensor error isolation) before the self-destroy sweeps — pinned by `ClearDatabaseServiceTests`. The sweep logs deferred/failed-load/not-removed counts at `Warn` with id samples, and `CheckSensorsHistoryAsync` logs per-product `TaskResult` failures instead of discarding them.
- **Still open:** sensors registered after the history check in the same tick are deferred for one sweep; `HasData` remains a cache-occupancy signal used as "sensor ever had data". A failed load is retried only by the bounded seam below, and only for self-destroy-enabled sensors — a sensor with self-destroy off still needs a process restart to recover its in-memory history. And the `LastTimeout`/`Storage.To` fallbacks are in-memory only: after a restart they survive only if the backing rows still exist in the database — a sensor whose rows were purged falls back to `CreationDate` again (same data, different outcome depending on process lifetime). Residuals are tracked in #1344 (item 2 — the non-monotonic `LastTimeout`/`Storage.To` fallback — is carried by #1345).
- **Failed-load retry (#1344):** the `Initialize()` failure latch (anti-retry-storm, #1296) is rerun by `BaseSensorModel<T>.RetryFailedHistoryLoad(utcNow)` — called only from the self-destroy sweep, never from the per-value paths, and at most `MaxHistoryLoadRetriesPerSweep` times per sweep (a whole-database outage latches many sensors at once; the cap keeps the serial sweep from turning into a synchronized burst of throwing reads — capped-out sensors are picked up by the next sweep and lose no retry budget). The cap counts retries that actually reached the database: `RetryFailedHistoryLoad` returns whether it ran, and a sensor merely waiting out its backoff is not charged — charging suppressed calls would let the sensors early in the (stable) enumeration order exhaust the cap sweep after sweep and permanently starve every sensor past it. Timing has two gates, both on the caller-supplied clock: the first retry must clear one sweep period measured from the **failure** itself (so the sweep that just observed the failure — including the eager `Initialize()` of `CheckSensorsHistoryAsync` one await earlier in the same maintenance tick — never hits the DB back-to-back); later retries back off from the previous retry — 2h, 4h, 8h, 16h, then a 24h cap. So an outage costs ~1h if the database recovers inside the first hour and degrades gradually after that, instead of jumping straight to a full day of disabled self-destroy; the cap keeps a permanently broken database at one attempt per sensor per day (#1296). The rerun mode is explicit (`LoadHistoryUnderLock(isRetry, utcNow)`), because a live sensor can have a null `LastValue` (timeout-only traffic, rejected values, retention purge) and "Storage looks empty" does not mean "no live ingestion": a retry is **write-free except for the restored activity signal** — `LastTimeout` (newest-wins marker restore) when the newest DB row is a timeout marker, or the `Storage.To` activity floor (`SetLastActivity`, kept in its own field and merged into `To` at read time, so it can be outgrown by live ingestion but never overwritten by it) when it is a real value; without the `To` floor a non-marker newest row would restore nothing, the latched load would let `ShouldDestroy()` fall through to `CreationDate`, and an established sensor whose newest value is minutes old would be deleted — **and `Cut(From)`**. It does not rebuild the value cache, does not restore `IsExpired`, does not re-arm TTL clocks and runs no policy fan-out — a retried sensor's self-destroy decision rides on the live cache, the restored marker or the restored `To`: with an empty cache it judges on the newest DB row just like a cleanly-loaded twin, except via the marker's `.Time`, which over-estimates last activity by up to one TTL and so destroys later, never earlier.
- **Verdict on the remaining unguarded readers** (`LastValue`, `LastDbValue`, `HasData` elsewhere, `LastUpdate`, `From`/`To`, `Result`, `Revalidate()`, `Cut()`, `Clear()`, #1325 inventory): none destroys data — they at worst show an empty/stale view during the startup window or act on it (e.g. a TTL revalidation against no last value is a no-op).

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
