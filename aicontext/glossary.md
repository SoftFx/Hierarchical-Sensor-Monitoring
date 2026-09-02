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
| API token | Personal opaque bearer credential (`hsm_pat_v1_<token-id>.<secret>`) for non-interactive management clients; only a SHA-256 verifier is persisted. | Distinct from collector access keys; creation/lifecycle is cookie-only. See `aicontext/features/server/api-tokens/`. |
| TokenId / EntityId | API-token identity: TokenId is the public 128-bit authentication lookup key (opaque, not a secret, but never disclosed by management responses); EntityId is the stable GUID used by lifecycle routes. | Lifecycle and list responses expose EntityId only — the TokenId matters solely inside the presented credential. |
| Token verifier | Domain-separated `SHA-256("HSM-API-TOKEN" ‖ 0x00 ‖ version ‖ tokenId ‖ secret)` stored instead of the secret. | Compared constant-time against stored-or-dummy on every authentication. |
| Token grant | Explicit operation + boundary (Global/Product/Folder) pair; pairs are never recombinable. | Unknown operations/boundaries fail closed; no wildcards in v1. |
| Revocation generation | Monotonic global/per-owner counter; a token authenticates only while its at-issue generations equal current values. | Emergency revoke-all/revoke-user advances it; missing-as-zero is the fresh baseline, corrupt/regressed fails closed. |
| HsmApiToken scheme | The dedicated ASP.NET authentication scheme for API-token bearers; never the default scheme, runs only from the management policy. | Cookie stays default and is pinned into the `DefaultPolicy`; a cookie-only principal never satisfies a management endpoint. |
| Management area (`/api/v1`) | The versioned management-API route family, SitePort-only and fail-closed: endpoints need `[ManagementApi]` plus their policy, everything else in the area is 404 by default. | `/api/v1/api-tokens` is the sole cookie-only family inside the area. |
| Authorization boundary | The Product/Folder/Global anchor a token grant binds to; resolved from the live hierarchy per request (a sensor inherits its product's CURRENT folder). | Folder boundaries cover current+future contents; Global grants are never wildcards over scoped targets. |
| C++ wrapper | Native wrapper surface under `src/wrapper`. | Keep parity with collector public APIs. |
| Ping module | External module under `src/module/HSMPingModule`. | Integration surface and deployment assumptions matter. |

## HSM Agent Terms

| Term | Meaning | Notes |
|---|---|---|
| HSM Agent | Standalone Windows-service product (`src/agent`) that hosts the native collector and streams a machine's metrics to an HSM Server. | Distinct from the collector library it hosts; epic #1167. |
| Agent bundle | Per-product zip an admin downloads from the server (signed `hsm-agent.exe` + generated `config.json` + install scripts). | The exe is byte-identical across downloads; only `config.json` differs. |
| Agent config | `config.json` read by the agent (server address + access key + sensor groups). | Schema lives in `src/agent` + `docs/hsm-agent.md`; the server generates it per product. |
| Agent connection URL | Admin server setting (`AgentConfig.ExternalConnectionUrl`) — the externally-reachable Sensor-API base baked into bundles. | Behind Docker/NAT the server can't infer it; blank falls back to the request host. |

## Deprecated / Avoid

| Avoid | Prefer | Why |
|---|---|---|
| Global scheduler | Collector scheduler | Scheduler ownership is per collector in current architecture. |
| Metric | Sensor or sensor value | HSM domain model is sensor-based. |
| Backend | HSM Server | Public docs should name the product component. |
| Timer task | Scheduled task | Matches collector scheduler terminology. |
