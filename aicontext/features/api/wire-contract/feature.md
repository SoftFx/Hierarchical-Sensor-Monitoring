# Feature: Sensor API Wire Contract

> Owner: shared | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: API - the frozen wire contract between collectors (any language) and HSMServer: enums, DTOs, JSON conventions, endpoints

---

## Overview

Everything in this file is **byte-for-byte compatibility-critical**: the .NET collector, the C++/CLI wrapper, the native C++ port, and the server must agree on these values. Source of truth: `src/api/HSMSensorDataObjects/`. Never renumber enums; add new members with new values only.

## Enums (numeric values are the contract)

`SensorType` (`SensorType.cs`): BooleanSensor=0, IntSensor=1, DoubleSensor=2, StringSensor=3, IntegerBarSensor=4, DoubleBarSensor=5, FileSensor=6, TimeSpanSensor=7, VersionSensor=8, RateSensor=9, EnumSensor=10.

`SensorStatus` (`SensorStatus.cs`): OffTime=0, Ok=1 (default), Warning=2, Error=3.

`Unit` (sparse, gaps intentional): bits=0, bytes=1, KB=2, MB=3, GB=4, Percents=100, Ticks=1000, Milliseconds=1010, Seconds=1011, Minutes=1012, Count=1100, Requests=1101, Responses=1102, Bits_sec=2100, Bytes_sec=2101, KBytes_sec=2102, MBytes_sec=2103, ValueInSecond=3000.

Alert enums (`SensorRequests/AddOrUpdateSensor/AlertUpdateRequest.cs`):

- `AlertOperation`: LessThanOrEqual=0, LessThan=1, GreaterThan=2, GreaterThanOrEqual=3, Equal=4, NotEqual=5, IsChanged=20, IsError=21, IsOk=22, IsChangedToError=23, IsChangedToOk=24, Contains=30, StartsWith=31, EndsWith=32, ReceivedNewValue=50.
- `AlertProperty`: Status=0, Comment=1, Value=20, Min=101, Max=102, Mean=103, Count=104, LastValue=105, FirstValue=106, Length=120, OriginalSize=151, NewSensorData=200, EmaValue=210, EmaMin=211, EmaMax=212, EmaMean=213, EmaCount=214.
- `AlertCombination`: And=0, Or=1. `TargetType`: Const=0, LastValue=1.
- `AlertRepeatMode`: FiveMinutes=5, TenMinutes=6, FifteenMinutes=7, ThirtyMinutes=10, Hourly=20, Daily=50, Weekly=100.
- `AlertDestinationMode`: DefaultChats=0 (obsolete), NotInitialized=1, Empty=2, FromParent=3, AllChats=200 — serialized in every `AlertUpdateRequest`.
- Flags: `StatisticsOptions { None=0, EMA=1 }`, `DefaultAlertsOptions { None=0, DisableTtl=1, DisableStatusChange=2 }`.
- Display units: `NoDisplayUnit`, `RateDisplayUnit { PerSecond=0 … PerMonth=5 }` → `AddOrUpdateSensorRequest.DisplayUnit (int?)`.
- Alert icons: `AlertIcon { Ok=0, Warning=1, Error=2, Pause=3, ArrowUp=10, ArrowDown=11, Clock=100, Hourglass=101 }`; `ThenSetIcon(AlertIcon)` maps to UTF-8 emoji strings (`IconExtensions.ToUtf8`) — the **string** is what goes on the wire in `Icon`.
- `EnumOption`: `{ Key:int, Value:string, Description:string, Color:int (ARGB) }`.

## Value DTOs (`SensorValueRequests/*`)

`SensorValueBase` (all values): `path`, `comment?`, `time` (UTC, defaults to now), `status` (default Ok). Typed descendants add `value`:

| DTO | Value type / format |
|---|---|
| `BoolSensorValue` | bool |
| `IntSensorValue` | int |
| `DoubleSensorValue` | double (NaN/Infinity permitted by serializer settings, rejected by collector validation) |
| `StringSensorValue` | string |
| `TimeSpanSensorValue` | `hh:mm:ss.fff` |
| `VersionSensorValue` | `a.b[.c[.d]]` |
| `EnumSensorValue` | int (option key) |
| `RateSensorValue` | double |
| `CounterSensorValue` | int |
| `IntBarSensorValue` / `DoubleBarSensorValue` | `Min/Max/Mean/Count/FirstValue?/LastValue/OpenTime/CloseTime` (+obsolete `Percentiles` — never populated, but serialized as `null` since nulls are not omitted) |
| `FileSensorValue` | `Value` = `List<byte>` → **numeric JSON array** (`[72,105,...]`, NOT base64 — System.Text.Json base64-encodes `byte[]` but not `List<byte>`), `Name`, `Extension` |

There is no Counter DTO: `CounterSensorValue.cs` is a legacy file name that contains `RateSensorValue`.

## Registration / command DTOs

`AddOrUpdateSensorRequest` (`SensorRequests/AddOrUpdateSensor/`): `path`, `sensorType?`, `description`, `keepHistory?`/`selfDestroy?`/TTLs (ticks), `statistics?`, `isSingletonSensor?`, `aggregateData?`, `enableGrafana?`, `originalUnit?`, `displayUnit?`, `isForceUpdate`, `enumOptions` (`EnumOption { key:int, value:string, description:string, color:int(ARGB) }`), `alerts` / `ttlAlerts` (`AlertUpdateRequest { conditions[{combination, operation, property, target{type,value}}], status, template, icon, isDisabled, confirmationPeriod?, scheduledNotificationTime?, scheduledRepeatMode?, scheduledInstantSend? }`), `defaultAlertsOptions`. Obsolete compat properties: `TTL`, `TtlAlert`, `DefaultChats`.

History DTOs (`HistoryRequests/`): `HistoryRequest { path, from, to?, count?, options }`, `FileHistoryRequest { +fileName="temp", extension="csv", isZipArchive }` — used by server tooling; the collector itself does not query history.

## JSON conventions

What the .NET collector actually emits (`Client/HttpsClient/RequestHandlers/HttpRequest.cs` — default System.Text.Json options + `AllowNamedFloatingPointLiterals` + polymorphic `JsonRequestConverter`, NO naming policy, NO ignore conditions):

- **PascalCase** property names (`"Path"`, `"Comment"`, `"OpenTime"` — exactly as declared in C#).
- **Nulls and defaults ARE emitted** (`"Comment":null`, `"Status":1`). The `[DefaultValue]` attributes on DTOs are Newtonsoft-era leftovers and have no effect under System.Text.Json. The server deserializes case-insensitively and tolerates omissions — but a byte-compatible port must match what the .NET collector sends, not what the server minimally accepts.
- Enums serialized as **numbers**.
- DateTime: ISO 8601; `Time` defaults to `DateTime.UtcNow` → `Z` suffix.
- TimeSpan: .NET "c" format `[-][d.]hh:mm:ss[.fffffff]` (days prefix possible; 7-digit fraction omitted when zero).
- Version: `a.b[.c[.d]]`.
- Polymorphic batch (`list` endpoint): items discriminated by the **numeric `Type` property** (`SensorType` value) — the server's converter scans for a property named `Type` (case-insensitive) and switches on its int value. There is no string discriminator.
- Registration time fields (`TTLs`, `KeepHistory`, `SelfDestroy`, alert `ConfirmationPeriod`) go on the wire as **`long` ticks** (`Converters/ApiConverters.cs`). When `TtlAlerts` are present their `TtlValue`s override `options.TTLs`. `IsSingletonSensor` is OR-ed with `IsComputerSensor` at conversion.

## Endpoints

Base `{scheme}://{server}:{port}/api/sensors/`; auth headers `Key: <AccessKey>`, `ClientName: <ClientName>`. All POST except `testConnection` (GET).

| Route | Payload |
|---|---|
| `bool` `int` `double` `string` `timespan` `version` `rate` `enum` | single typed value |
| `intBar` `doubleBar` | single bar value |
| `list` | polymorphic batch |
| `file` | `FileSensorValue` |
| `commands` | command batch (response: error dictionary keyed by sensor path) |
| `addOrUpdate` | `AddOrUpdateSensorRequest` |
| `testConnection` | — |

## Invariants

- Never renumber or reuse enum values; never rename JSON fields; additive evolution only.
- Server tolerates unknown/omitted optional fields; collectors must tolerate unknown response fields.

## Native port (C++)

The native collector (`src/native/collector`, #1096) reproduces this wire **byte-for-byte** against the **net8 / Core** `System.Text.Json` output (the shortest-double runtime; net472 doubles diverge and are out of scope, as in `number_format_contract`). `BuildWire{Value,Bar,File,Registration}Json` in `hsm_collector.cpp` emit the exact property order (most-derived-first, base-last, `Type` first), `Key:null`, ISO-8601-Z time (fraction trimmed), TimeSpan ".NET c", `List<byte>` numeric array, `Percentiles:null`, and the full `AddOrUpdateSensorRequest` shape. Parity is locked from both sides: native `native_wire_*` unit tests pin the exact bytes, and `WireFormatGoldenLockTests` (net8 IntegrationTests) asserts the **same** strings against the real `HttpRequest<T>` serializer — if .NET drifts, that test fails first and both sides update in lockstep.

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMSensorDataObjects/**` | DTOs + enums (source of truth) |
| `src/collector/HSMDataCollector/Client/HttpsClient/Endpoints.cs` | Route constants |
| `src/collector/HSMDataCollector/Converters/*.cs` | Serializer configuration |
| `ai-docs/Wiki/REST-API.md` | Human-facing examples |

## Dependencies

- Used by: collector `data-pipeline`/`http-client`, server controllers, `src/wrapper`, native C++ port.
