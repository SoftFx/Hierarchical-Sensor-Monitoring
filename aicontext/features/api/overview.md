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
| `/api/sensors/history` | POST | The values of one sensor (`HistoryRequest`) as JSON; a key that can read sensor data |
| `/api/sensors/historyFile` | POST | The same as a CSV (or text) file, optionally zipped (`FileHistoryRequest`) |

### A large file value in the history (#1885)

A File sensor may hold large file values (a file whose content the server keeps in its large file store, ADR-0022). An access key reads their description, never their content; the archive is downloaded only through the management API or the web UI (`../server/large-file-download/feature.md`).

- `history`: the value keeps the members of a file value (`Comment`, `Time`, `Status`, `Value`, `FileName`, `Extension`) with an empty `Value`, and gets an additive member `LargeFile { FileId, Name, Extension, Size, Secure, State }`. `Secure` and `State` come from the store's record: `State` is `verified`, `packed`, `removed` (the store no longer holds the file; `Secure` is null) or `unavailable` (the store could not be read; the rest of the history is still answered). An ordinary file value has no `LargeFile` member: its JSON is what it was.
- `historyFile`: the content cell of a large value is empty, and a last column `Large file` holds `Large file <fileId>; <size> bytes; secure|not secure; <state>`. The column is present only when the values hold a large one, so the columns of a File sensor without one are what they were.
- `historyFile` writes every value as it is stored, each cell quoted, a quote in it doubled: a String value, a comment, a file's name and extension and its base64 content are not changed for a spreadsheet (no apostrophe before `=`, `+`, `-`, `@`; bidirectional control characters kept). Only the web UI's download of the History tab writes a collector's free text as spreadsheet text (`../server/file-sensor-page/feature.md`, History export).

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
- `FileSensorValue` adds `Value` (bytes), `Name`, `Extension`. A File sensor may also hold **large file values** (ADR-0022, #1883): uploaded in pieces over the file connection (#1884), never through this DTO. Stored as `LargeFileValue` rows (the description and the file id, no content; MemoryPack union tag 12, so ordinary file rows keep their bytes, and a server without the type loads the tree and skips only those rows): `aicontext/features/storage/large-files/feature.md`
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
| Table block format | `src/api/HSMTableBlocks` | Byte format shared by collectors, server and clients; versioned, golden blocks. |

## Feature Folders

- [`wire-contract/`](./wire-contract/feature.md) — the frozen collector↔server wire contract (enums, DTOs, JSON conventions, endpoints).
- [`table-block/`](./table-block/feature.md) — the table block: what a collector sends for table sensors (ADR-0021); byte layout, limits, error codes, versioning, and what a writer in another language must do.
- [`alert-administration/`](./alert-administration/feature.md) — the `/api/v1` REST surface for administering alerts (#1500): data-policy and TTL-policy CRUD on sensors and products, bearer-token authenticated.

To add as work lands (from `../_TEMPLATE_feature.md`):

- `history-requests/` - history query contracts.
- `wrapper-api/` - C++ wrapper compatibility contract.

(Collector public API is documented at [`../collector/public-api/feature.md`](../collector/public-api/feature.md).)
