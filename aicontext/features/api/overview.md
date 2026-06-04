# API Overview

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

## Purpose

The Sensor API is the data contract between DataCollector (client) and HSMServer. DataCollector POSTs typed sensor values to REST endpoints; the server also sends commands back.

## Base URL

```
https://{server}:44330/api/sensors/
```

## Authentication

HTTP headers on every request:
- `ClientName` — human-readable client identifier
- `Key` — product access key (API key)

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
| `/api/sensors/addOrUpdate` | POST | Register or update sensor metadata |
| `/api/sensors/commands` | POST | Retrieve pending commands from server |

## Shared DTOs

All DTOs live in `HSMSensorDataObjects` (shared library):

- `SensorValueBase` — base class with `Key` (sensor path), `Time`, `Status`, `Comment`
- Typed descendants add the `Value` field of the appropriate type
- `BarSensorValueBase` adds `Min`, `Max`, `Mean`, `Count`, `OpenTime`, `CloseTime`
- `FileSensorValue` adds `Value` (bytes), `Name`, `Extension`
- `CommandRequestBase` — server-to-collector command

## Sensor Path Convention

```
{ComputerName}/{Module}/{SensorPath}
```

Example: `PROD-SERVER-01/MyService/CPU Usage`

## Web UI API

The Web UI (:44333) has separate MVC controllers for user-facing operations (not documented here — those are standard ASP.NET MVC routes, not part of the collector API).

## Swagger

Available at `https://{server}:44330/api/swagger` when running in development mode.

## Contract Areas

| Area | Code | Notes |
|---|---|---|
| Sensor DTOs | `src/api/HSMSensorDataObjects` | Serialization compatibility matters. |
| Server controllers | `src/server/HSMServer/Controllers` | Keep request/response docs aligned. |
| Collector public API | `src/collector/HSMDataCollector/PublicAPI`, `Core/IDataCollector.cs`, `Core/DataCollector.cs`, and `Options` | Breaking changes affect integrators. |
| C++ wrapper headers | `src/wrapper/include` | Keep parity with collector public API. |

## Feature Folders To Add Here

- `sensor-requests/` - sensor registration/value request contracts.
- `history-requests/` - history query contracts.
- `collector-public-api/` - collector interfaces/options contracts.
- `wrapper-api/` - C++ wrapper compatibility contract.

Create folders from `../_TEMPLATE_feature.md` as work lands.
