# REST API

HSM exposes an HTTP API for sending sensor data and querying history. Use it from any language or environment where the NuGet package is not available.

**Base URL:** `https://your-hsm-server:44330/api/sensors`

All requests use **HTTPS**. Authentication is via the `Key` header.

---

## Authentication

Include the access key in every request header:

```
Key: YOUR_ACCESS_KEY
```

The access key is generated per product in the web UI under **Products → Access Keys**.

Alternatively, the key can be passed in the request body as the `key` field (not recommended — prefer the header).

---

## Send Sensor Values

All endpoints accept `POST` with `Content-Type: application/json`.

### Bool

`POST /api/sensors/bool`

```json
{
  "path": "MyApp/is_running",
  "value": true,
  "comment": "optional comment",
  "status": 0
}
```

### Int

`POST /api/sensors/int`

```json
{
  "path": "MyApp/queue_depth",
  "value": 42
}
```

### Double

`POST /api/sensors/double`

```json
{
  "path": "MyApp/cpu_usage",
  "value": 87.3,
  "comment": "after spike"
}
```

### String

`POST /api/sensors/string`

```json
{
  "path": "MyApp/last_error",
  "value": "Connection timeout after 30s"
}
```

### TimeSpan

`POST /api/sensors/timespan`

```json
{
  "path": "MyApp/request_duration",
  "value": "00:00:00.350"
}
```

Value format: `hh:mm:ss.fff` (standard .NET TimeSpan string).

### Version

`POST /api/sensors/version`

```json
{
  "path": "MyApp/version",
  "value": "2.1.0"
}
```

### Rate

`POST /api/sensors/rate`

```json
{
  "path": "MyApp/requests_per_sec",
  "value": 150.5
}
```

### IntegerBar

`POST /api/sensors/intBar`

```json
{
  "path": "MyApp/response_time_ms",
  "min": 45,
  "max": 320,
  "mean": 112.5,
  "count": 200,
  "openTime": "2025-03-22T09:00:00Z",
  "closeTime": "2025-03-22T09:05:00Z"
}
```

### DoubleBar

`POST /api/sensors/doubleBar`

```json
{
  "path": "MyApp/cpu_samples",
  "min": 12.1,
  "max": 95.4,
  "mean": 43.7,
  "count": 300,
  "openTime": "2025-03-22T09:00:00Z",
  "closeTime": "2025-03-22T09:05:00Z"
}
```

### File

`POST /api/sensors/file`

```json
{
  "path": "MyApp/daily_report",
  "fileName": "report_2025-03-22",
  "extension": "csv",
  "value": "BASE64_ENCODED_FILE_CONTENT"
}
```

`value` is the file content encoded as Base64.

---

## Send Multiple Values

`POST /api/sensors/list`

Send up to 1000 values in a single request. Each object must include a `type` discriminator:

```json
[
  { "type": "bool",   "path": "MyApp/alive",     "value": true },
  { "type": "double", "path": "MyApp/cpu",        "value": 45.2 },
  { "type": "int",    "path": "MyApp/queue",      "value": 17 },
  { "type": "string", "path": "MyApp/status",     "value": "OK" }
]
```

Valid `type` values: `bool`, `int`, `double`, `string`, `timespan`, `version`, `rate`, `intBar`, `doubleBar`, `file`.

---

## Create or Update Sensor Metadata

`POST /api/sensors/addOrUpdate`

Pre-create a sensor or update its metadata (description, TTL, etc.) without sending a value:

```json
{
  "path": "MyApp/cpu_usage",
  "sensorType": "double",
  "description": "CPU usage of the main process",
  "unit": "Percent",
  "ttl": "00:05:00",
  "keepHistory": "30.00:00:00"
}
```

---

## Query History

`POST /api/sensors/history`

Retrieve historical values for a sensor. The access key must have **read** permission.

**By time range:**
```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "to":   "2025-03-22T23:59:59Z"
}
```

**By count (last N values):**
```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "count": 100
}
```

Returns a JSON array of historical values with timestamps.

---

## Export History as File

`POST /api/sensors/historyFile`

Same as history, but returns a CSV or TXT file:

```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "to":   "2025-03-22T23:59:59Z",
  "fileName": "cpu_export",
  "extension": "csv",
  "isZipArchive": false
}
```

Set `"isZipArchive": true` to receive the file compressed as `.zip`.

---

## Test Connection

`GET /api/sensors/testConnection`

Returns `200 OK` if the server is reachable. No authentication required.

```bash
curl https://your-hsm-server:44330/api/sensors/testConnection
```

---

## HTTP Status Codes

| Code | Meaning |
|---|---|
| `200 OK` | Value accepted successfully |
| `400 Bad Request` | Malformed request body |
| `406 Not Acceptable` | Invalid access key, wrong product, or sensor type mismatch |

---

## Common Request Fields

All single-value endpoints share these optional fields:

| Field | Type | Description |
|---|---|---|
| `path` | string | Sensor path within the product (e.g. `"Service/cpu"`) |
| `comment` | string | Optional text attached to this value |
| `status` | int | `0` = Ok, `1` = Error (overrides policy-computed status) |
| `time` | string | ISO 8601 UTC timestamp (defaults to server receive time) |
| `key` | string | Access key (use header instead) |

---

## See Also

- [Home](Home) — Main documentation entry point
- [HSM DataCollector](HSMDataCollector) — NuGet package for .NET applications (preferred over REST API)
- [Access Keys](Access-keys) — Managing access keys and permissions
- [Sensor Types](Sensor-types) — Available sensor types and their properties
- [Core Concepts](Core-Concepts) — Fundamental HSM building blocks
