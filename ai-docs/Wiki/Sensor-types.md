# Sensor Types

HSM supports multiple sensor types for different monitoring scenarios. Each sensor type has its own set of available alert conditions.

See also: [Core Concepts → Sensor Types](Core-Concepts#sensor-types), [Alert Conditions Reference](Alert-Conditions-Reference)

---

## Simple Sensors

| Type | Value | Description |
|---|---|---|
| **Bool** | `true` / `false` | [Possible values](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/bool) |
| **Int** | Integer | [Possible values](https://learn.microsoft.com/en-us/dotnet/api/system.int32?view=net-7.0) |
| **Double** | Floating point | [Possible values](https://learn.microsoft.com/en-us/dotnet/api/system.double?view=net-7.0) |
| **String** | Text | [Possible values](https://learn.microsoft.com/en-us/dotnet/api/system.string?view=net-7.0) |
| **TimeSpan** | Duration | [Possible values](https://learn.microsoft.com/en-us/dotnet/api/system.timespan?view=net-7.0) — Added in [v3.15.0](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/releases/tag/server-v3.15.0) |
| **Version** | Version number | [Possible values](https://learn.microsoft.com/en-us/dotnet/api/system.version?view=net-7.0) — Added in [v3.18.0](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/releases/tag/server-v3.18.0) |
| **Rate** | Double as rate/speed | Requests per second, KB/sec, etc. |
| **Enum** | Integer mapped to names | State machine, mode selection |

## Bar Sensors

Bar sensors aggregate multiple values over a configurable time window (default: 5 minutes) and produce Min, Max, Mean, Count, First, and Last statistics.

| Type | Description |
|---|---|
| **IntegerBar** | A bar that contains [int](https://learn.microsoft.com/en-us/dotnet/api/system.int32?view=net-7.0) Min, Max, Mean, Last Value properties for some period of time |
| **DoubleBar** | A bar that contains [double](https://learn.microsoft.com/en-us/dotnet/api/system.double?view=net-7.0) Min, Max, Mean, Last Value properties for some period of time |

## Advanced Sensors

| Type | Description |
|---|---|
| **File** | Any file that can be converted to a byte stream |

---

## See Also

- [Core Concepts → Sensor Types](Core-Concepts#sensor-types) — Detailed explanation of each sensor type
- [Alert Conditions Reference](Alert-Conditions-Reference) — Which conditions are available for each sensor type
- [HSM DataCollector → Custom Sensors](HSMDataCollector#custom-sensors) — How to create sensors in code
- [REST API](REST-API) — How to send sensor values via HTTP
