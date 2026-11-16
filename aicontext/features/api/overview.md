# API Overview

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

## Purpose

The Sensor API is the data contract between DataCollector (client) and HSMServer. DataCollector POSTs typed sensor values to REST endpoints; the server also sends commands back.

## Base URL

```
https://{server}/api/sensors/
```

## Authentication

HTTP headers on every request:
- `ClientName` — human-readable client identifier
- `Key` — product access key (API key)

A rejected key answers `406 Not Acceptable` with its reason as the plain-text
body: `Key is invalid.` (unknown key), `AccessKey expired.`, `AccessKey is
blocked.`, or `AccessKey doesn't have {permission}.` — and the rejection logs
one WARN entry (no stack trace), never an ERROR (#1649). The answer is the
same whether the key rides the header (rejected by the telemetry middleware
before MVC runs) or the body (rejected through the action's own result).

## Sensor Value Endpoints

All accept POST with JSON body.

| Endpoint | Type | Request Body |
|---|---|---|
| `/api/sensors/bool` | Boolean | `BoolSensorValue` |
| `/api/sensors/int` | Integer | `IntSensorValue` |
| `/api/sensors/double` | Double | `DoubleSensorValue` |
| `/api/sensors/string` | String | `StringSensorValue` |
| `/api/sensors/timespan` | TimeSpan | `TimeSpanSensorValue` |
| `/api/sensors/version` | Version | `VersionSensorValue` |
| `/api/sensors/rate` | Rate | `RateSensorValue` |
| `/api/sensors/intBar` | Int Bar | `IntBarSensorValue` |
| `/api/sensors/doubleBar` | Double Bar | `DoubleBarSensorValue` |
| `/api/sensors/file` | File | `FileSensorValue` |
| `/api/sensors/list` | Batch | `List<SensorValueBase>` |

## Other Endpoints

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/sensors/testConnection` | GET | Check connectivity + auth |
| `/api/sensors/addOrUpdate` | POST | Register one sensor or update its metadata |
| `/api/sensors/commands` | POST | A batch of commands from the collector; today every command is a sensor registration. Answers a dictionary of errors keyed by sensor path |

### Statuses of the two registration routes (#1749)

A collector takes a **4xx** on a registration as a final refusal and never sends that package again; it sends a **5xx** (or a request with no answer) again on its next cycle. The routes answer accordingly:

| Status | `commands` | `addOrUpdate` |
|---|---|---|
| `200` | applied; a registration the server refuses has its reason under its path, the rest of the batch is applied | applied |
| `400` | the body cannot be read, or the list has a `null` element or a command without a path; nothing is applied | the body cannot be read |
| `406` | the key in the `Key` header is rejected, or the request has no key at all | the key is rejected or missing, or the server refuses the registration |
| `500` | some commands faulted (the server's own fault: an exception, a fault of the sensor cache or a failed write to the store, #1756), the others were applied; send the batch again. The body holds the per-path errors of the refused commands. Also when the server failed while looking the access key up | the server's own fault while it applied the registration or looked the access key up |

The full table, what a collector does with each row, and what is not covered: [`wire-contract/feature.md`](./wire-contract/feature.md#statuses-of-the-registration-routes-1749).

## Shared DTOs

All DTOs live in `HSMSensorDataObjects` (shared library):

- `SensorValueBase` — base class with `Path`, `Time`, `Status`, `Comment`
- Typed descendants add the `Value` field of the appropriate type
- `BarSensorValueBase` adds `Min`, `Max`, `Mean`, `Count`, `OpenTime`, `CloseTime`; the concrete `IntBarSensorValue` / `DoubleBarSensorValue` add `StdDev?` (#1509, optional, `null`/absent = unknown — sent by the native collector only, #1529)
- `FileSensorValue` adds `Value` (bytes), `Name`, `Extension`
- `CommandRequestBase` — collector-to-server command (registration etc.)

Full wire contract (enum numeric values, JSON conventions, every DTO property): [`wire-contract/feature.md`](./wire-contract/feature.md).

## Sensor Path Convention

```
{ComputerName}/{Module}/{SensorPath}
```

Example: `PROD-SERVER-01/MyService/CPU Usage`

## Web UI API

The Web UI has separate MVC controllers for user-facing operations (not documented here — those are standard ASP.NET MVC routes, not part of the collector API).

## Swagger

Available at `https://{server}/api/swagger` (the single port serves every surface, #1590).

## Contract Areas

| Area | Code | Notes |
|---|---|---|
| Sensor DTOs | `src/api/HSMSensorDataObjects` | Serialization compatibility matters. |
| Server controllers | `src/server/HSMServer/Controllers` | Keep request/response docs aligned. |
| Collector public API | `src/collector/HSMDataCollector/PublicAPI`, `Core/IDataCollector.cs`, `Core/DataCollector.cs`, and `Options` | Breaking changes affect integrators. |
| C++ wrapper headers | `src/wrapper/include` | Keep parity with collector public API. |

## Feature Folders

- [`wire-contract/`](./wire-contract/feature.md) — the frozen collector↔server wire contract (enums, DTOs, JSON conventions, endpoints).
- [`alert-administration/`](./alert-administration/feature.md) — the `/api/v1` REST surface for administering alerts (#1500): data-policy and TTL-policy CRUD on sensors and products, bearer-token authenticated.

To add as work lands (from `../_TEMPLATE_feature.md`):

- `history-requests/` - history query contracts.
- `wrapper-api/` - C++ wrapper compatibility contract.

(Collector public API is documented at [`../collector/public-api/feature.md`](../collector/public-api/feature.md).)
