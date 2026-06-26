# System Architecture Overview

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

## Components

```
+---------------------+          HTTPS (:44330)          +---------------------+
|                     |  ----> /api/sensors/{type} ----> |                     |
|  HSMDataCollector   |  <---- /api/sensors/commands <-- |     HSMServer       |
|  (NuGet library)    |                                  |  (ASP.NET Core MVC) |
|                     |                                  |                     |
+---------------------+                                  +---------------------+
  Embedded in user app                                     |          |
                                                           |  Web UI  |
                                                      :44333  (:44333)
                                                           |          |
                                                     +-----+----------+-----+
                                                     |      LevelDB         |
                                                     |   (sensor history)   |
                                                     +-----------------------+
```

## HSMDataCollector (client library)

NuGet package embedded into monitored .NET applications. Responsibilities:
- Create and manage sensors (bar, rate, function, instant, file)
- Collect default system metrics (CPU, RAM, disk, threads, GC, service status)
- Queue sensor values and send them in batches to HSMServer
- Handle connection failures with Polly retry
- Lifecycle management: Start -> Running -> Stop -> Disposed

Key internals:
- `CollectorScheduler` — static timer wheel for all periodic actions (sensor reads, bar ticks, disk predictions)
- `DataProcessor` — owns 4 queue processors (data, priority, file, command) and routes values
- `HsmHttpsClient` — HTTP client with configurable timeout and optional TLS certificate bypass
- `MessageDeduplicator` — bounds error log volume from recurring exceptions

Target frameworks: `net6.0` + `net472`.

## HSMServer (web application)

ASP.NET Core 8.0 MVC application. Responsibilities:
- Receive sensor data via REST API
- Store sensor history in LevelDB
- Provide web dashboard with hierarchical tree view
- Manage products, access keys, users, roles
- Configure alerts with conditions, schedules, and notification channels (Telegram, email)
- Serve Grafana-compatible datasource API
- Background services: data collection, snapshots, cleanup, notifications, backups

Key internals:
- `TreeValuesCache` — in-memory tree of products -> nodes -> sensors
- `UpdatesQueue` — Channel-based async queue for processing incoming sensor updates
- `ConcurrentStorage<T>` — thread-safe storage pattern synced with LevelDB
- `BaseSensorModel<T>` — typed sensor model with policies (TTL, alerts)

## HSMSensorDataObjects (shared DTOs)

Shared C# library defining the data contract between collector and server:
- `SensorValueBase` and typed descendants (BoolSensorValue, IntSensorValue, etc.)
- `CommandRequestBase` for server->collector commands
- `SensorRequests` for sensor registration and metadata

## Database (LevelDB)

Embedded key-value store via LightningDB. No external database service required.
- `HSMDatabase` — abstract interfaces
- `HSMDatabase.LevelDB` — LevelDB implementation
- `HSMDatabase.AccessManager` — data access layer with entity formatters

## HSMPingModule

Standalone console application that uses DataCollector to monitor network connectivity (ping, VPN status).

## Data Flow

1. User app creates `DataCollector` with `CollectorOptions` (server address, access key)
2. User creates sensors (bar, rate, function, instant) or enables default sensors
3. `CollectorScheduler` fires periodic reads; sensors produce `SensorValueBase` values
4. Values are queued in `ConcurrentQueue` inside `QueueProcessorBase`
5. `QueueProcessorBase.ProcessingLoop` dequeues batches and sends via `IDataSender`
6. `HsmHttpsClient` POSTs JSON to HSMServer API endpoints
7. Polly retries on failure with exponential backoff
8. HSMServer receives values, updates `TreeValuesCache`, stores in LevelDB
9. Web UI renders the sensor tree with current values, history charts, alerts
