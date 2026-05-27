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

Initialization is expected before values are accepted. Changes around `TryAddValue`, `TryUpdateLastValue`, TTL, or policy loading should include tests for first-value initialization and timeout behavior.

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
