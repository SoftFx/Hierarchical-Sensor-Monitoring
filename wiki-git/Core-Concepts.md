# Core Concepts

This page explains the fundamental building blocks of HSM.

---

## Sensor

A **sensor** is the basic unit of monitoring. It has a **path**, a **type**, and stores a stream of values over time.

### Path

The path identifies a sensor within a product. It is a slash-separated string:

```
ServiceName/component/metric
```

Examples:
```
MyService/database/query_time_ms
MyService/api/requests_per_second
worker-1/process/memory_mb
```

Rules:
- The string after the last `/` is the sensor name
- Everything before it forms the folder hierarchy
- Paths are unique within a product
- A new sensor is created automatically the first time data is sent to a new path
- Maximum path depth: 10 levels (configurable by admins)

### Key

Every request to the API must include an **Access Key**. The key identifies which product the incoming data belongs to and what permissions the caller has.

Keys are created per product in the web UI under **Products → Access Keys**.

---

## Product

A **Product** is the top-level container for sensors. It represents one application, service, or system being monitored.

Each product has:
- One or more **Access Keys** for data ingestion
- A **sensor tree** of all paths that have sent data
- **Default alert chats** — Telegram chats that receive alerts by default

---

## Folder

**Folders** group products together. They are used to:
- Organize products by team, environment, or domain
- Link **Telegram chats** — a chat connected to a folder receives alerts from all products in that folder
- Apply **Access Key** permissions at a higher level

---

## Sensor Status

Every sensor has a computed status based on its alert policies:

| Status | Icon | Meaning |
|---|---|---|
| **Ok** | 🟢 | No active alerts |
| **Error** | 🔴 | At least one alert policy condition is active |
| **OffTime** | 🕑 | Sensor has not sent data within its TTL window |

Status priority (lowest to highest): `OffTime → Ok → Error`

A parent node shows the highest-priority status of all sensors below it.

---

## Sensor State

Separate from status, a sensor also has a **state** that controls whether it is active:

| State | Meaning |
|---|---|
| **Available** | Normal operation |
| **Muted** | Sensor is active but all alerts are suppressed |
| **Blocked** | Sensor is permanently disabled |

---

## Sensor Types

HSM supports 11 sensor types:

| Type | Value | Use case |
|---|---|---|
| `Bool` | true / false | Service alive, feature flag, binary state |
| `Int` | integer | Queue depth, thread count, error count |
| `Double` | floating point | CPU %, memory MB, response time |
| `String` | text | Last error message, status string |
| `TimeSpan` | duration | Request duration, uptime |
| `Version` | version number | Application version (e.g. `2.1.0`) |
| `Rate` | double as rate/speed | Requests per second, KB/sec |
| `Enum` | integer mapped to names | State machine, mode selection |
| `IntegerBar` | aggregated int | Response times over 5-min window |
| `DoubleBar` | aggregated double | CPU samples over 5-min window |
| `File` | file content | Reports, logs, configuration snapshots |

**Bar sensors** collect many individual measurements over a configurable time window (default: 5 minutes) and send aggregate statistics: min, max, mean, count, first value, last value.

---

## Value Comment and Status

Every sensor value can carry an optional **comment** (free text) and an explicit **status** override:

- `status: 0` — Ok
- `status: 1` — Error

If status is not set, HSM computes it from the active alert policies.

---

## Access Key Permissions

Access keys have two permission levels:

| Permission | Can do |
|---|---|
| **Send data** | POST sensor values via API |
| **Read data** | GET sensor history via API |

A key can have one or both permissions. Keys are managed per product by users with Manager role.

---

## TTL (Time-To-Live)

TTL is a timeout after which a sensor is considered inactive. If no value arrives within the TTL window, the sensor status changes to **OffTime** and a TTL alert fires (if configured).

TTL can be set:
- Per sensor in the sensor settings
- Via `SensorOptions.TTL` in the DataCollector
- Via an Alert Template applied to many sensors at once

See [Alerts Overview](Alerts-Overview) for TTL alert configuration.
