# HSM Glossary

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

Canonical terms for Hierarchical-Sensor-Monitoring. Prefer these names in code,
docs, PR descriptions, review comments, and user-facing documentation.

## Core Product Terms

| Term | Meaning | Notes |
|---|---|---|
| HSM | Hierarchical-Sensor-Monitoring product and repository. | Use `HSM` after first expansion when useful. |
| Sensor | A monitored value source identified by a hierarchical path. | Avoid using "metric" when the code/API uses sensor semantics. |
| Bar sensor | Aggregating sensor that collects min/max/mean/count over a time window. | Keep bar period and post period semantics explicit. |
| Rate sensor | Sensor that accumulates values and reports a rate over a configured period. | Check zero/NaN handling when changing accumulation. |
| Function sensor | Sensor that periodically calls user-provided code and sends the result. | User exceptions must be isolated. |
| Instant sensor | Sensor where the integrator explicitly calls `AddValue()` / `SendValue()`. | Public methods can be called from any thread. |
| Default sensors | Built-in sensors for CPU, RAM, disk, threads, GC, service status, and related metrics. | Platform-specific behavior belongs in collector/default-sensor docs. |
| Sensor path | Full logical path of a sensor in the hierarchy. | Use for identity and navigation. |
| Sensor value | A concrete value sent by a sensor at a point in time. | Includes status and optional comment depending on DTO. |
| Sensor status | Health/status marker attached to a sensor or value. | Examples include OK/error-like states from `SensorStatus`. |
| Product | Server-side logical group of sensors, usually identified by an access key. | Keep distinct from product/application code naming. |
| Node | Hierarchical folder in the sensor tree. | Path example: `Computer/Module/SensorPath`. |
| Module | Logical group under a collector/client that owns sensors. | Do not confuse with .NET project/module. |
| Environment | Server-side scope/grouping used to organize monitored systems. | Confirm exact behavior in server docs before changing. |
| Dashboard | Operator-facing view for monitoring selected HSM data. | Use for UI dashboards, not arbitrary charts. |

## Collector Terms

| Term | Meaning | Notes |
|---|---|---|
| DataCollector | .NET library entry point used by applications to register sensors and send values. | Public API compatibility matters. |
| DataProcessor | Internal collector component that owns queues, sensor storage, deduplication, and sending flow. | Internal term; avoid in user docs unless troubleshooting internals. |
| Sensor storage | Collector-owned registry of sensors. | Prefer `SensorsStorage` only when referring to the class. |
| Collector scheduler | Per-collector scheduler used for periodic sensor work. | Avoid "global timer" for new docs. |
| Sync queue | Collector queue that buffers values before send. | Document lifecycle and backpressure when changed. |
| Message deduplicator | Collector helper that groups repeated error/log messages within a time window. | Use when describing error-noise reduction. |
| Package sending info | Result metadata for sending a batch, including content size and optional error. | Code term: `PackageSendingInfo`. |
| Polly pipeline | Retry pipeline wrapping HTTP requests. | Watch `ShouldHandle` behavior for HTTP status codes. |

## Server And Storage Terms

| Term | Meaning | Notes |
|---|---|---|
| HSM Server | ASP.NET server application hosting API, site, background services, and storage access. | Use instead of just "backend" in public docs. |
| HSM Server Core | Shared server domain/core project. | Code/project term. |
| LevelDB storage | On-disk database implementation used by HSM. | Mention LMDB/native dependencies only when relevant. |
| Snapshot | Persisted or cached representation of current tree/state. | Be explicit: tree snapshot, sensor snapshot, or database snapshot. |
| Journal | Historical sequence of sensor values or changes. | Confirm file/class-specific semantics before broad edits. |
| Update queue | Server-side queue for propagating sensor/tree changes. | Treat ordering and idempotency as important. |

## Alerts And Notifications

| Term | Meaning | Notes |
|---|---|---|
| Alert | Rule-driven notification condition for monitored sensors. | Use "alert" for rule/notification concept. |
| Global alert | Alert defined via an `AlertTemplate` (wildcard path + folder + sensor type) that auto-applies to matching sensors. | Canonical mechanism for non-leaf alerting since #1142. |
| Per-sensor alert | Alert attached to a single sensor via the `_Alerts.cshtml` editor. | Supported path; templates materialize onto sensors as per-sensor policies tagged with `TemplateId`. |
| Node-level alert (removed) | Legacy alert attached directly to a Folder/Product via the per-node editor. | Removed in #1142; replaced by global alerts. Storage cleanup migration prunes dangling rows. |
| Alert template | Reusable alert configuration/template. | Keep distinct from a concrete alert instance if code does. |
| Alert schedule | Time window or schedule controlling alert activity. | Time zone and boundary behavior need tests. |
| Notification | Delivered message via Telegram/email or other channel. | Use when discussing delivery, retries, and failures. |
| TTL | Time-to-live policy on a sensor. | Expiration and status transitions should be tested. |

## API And Integration Terms

| Term | Meaning | Notes |
|---|---|---|
| DTO | Public data transfer object, often in `HSMSensorDataObjects`. | Serialization compatibility matters. |
| Access key | Credential/key used by collectors or clients to connect/send data. | Treat as sensitive. |
| C++ wrapper | Native wrapper surface under `src/wrapper`. | Keep parity with collector public APIs. |
| Ping module | External module under `src/module/HSMPingModule`. | Integration surface and deployment assumptions matter. |

## Deprecated / Avoid

| Avoid | Prefer | Why |
|---|---|---|
| Global scheduler | Collector scheduler | Scheduler ownership is per collector in current architecture. |
| Metric | Sensor or sensor value | HSM domain model is sensor-based. |
| Backend | HSM Server | Public docs should name the product component. |
| Timer task | Scheduled task | Matches collector scheduler terminology. |
