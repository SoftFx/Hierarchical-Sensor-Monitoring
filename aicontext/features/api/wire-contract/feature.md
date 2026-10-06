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
| `IntBarSensorValue` / `DoubleBarSensorValue` | `Min/Max/Mean/StdDev?/Count/FirstValue?/LastValue/OpenTime/CloseTime` (+obsolete `Percentiles` — never populated, but serialized as `null` since nulls are not omitted). `StdDev` (#1509): see below |
| `FileSensorValue` | `Value` = `List<byte>` → **numeric JSON array** (`[72,105,...]`, NOT base64 — System.Text.Json base64-encodes `byte[]` but not `List<byte>`), `Name`, `Extension` |

There is no Counter DTO: `CounterSensorValue.cs` is a legacy file name that contains `RateSensorValue`.

### Bar StdDev (#1509)

**Compatible-additive wire change** (DTO `HSMSensorDataObjects` 3.2.0, managed collector 3.7.0, native
collector 0.11.0): `BarSensorValueBase<T>` gained `StdDev` (`double?`), serialized right after `Mean`:

```
{"Type":4,"Min":1,"Max":5,"Mean":3,"StdDev":1.41,"FirstValue":1,"LastValue":5,"Percentiles":null,...}
```

- Meaning: the **population** standard deviation of the bar's samples, `sqrt(Σ(x − mean)² / Count)`;
  `0` for a single sample. A double on **both** bar flavors (an int bar posts `1.41`, not `1`).
- Accumulation (identical in both collectors, so identical samples give identical bytes): Welford, per
  sample before `Count` is incremented — `n = Count + 1; delta = x − m; m += delta / n; M2 += delta * (x − m)`
  — and on post `σ = Count <= 1 ? 0 : sqrt(M2 / Count)`. O(1) per sample, no sample storage, no
  cancellation for a large offset with a small spread (a Σx/Σx² form would lose it). The running `m` is
  separate from the sum behind the wire `Mean`, which is unchanged. The native build disables
  floating-point contraction (`-ffp-contract=off` on GCC/Clang) so no compiler fuses `delta * (x − m) + M2`
  into an FMA that RyuJIT would not.
- Rounding: double bar — the bar precision, half away from zero (`Math.Round(σ, Precision,
  AwayFromZero)`, like `Mean`); int bar — a fixed 2 digits, half away from zero (an int bar has no
  precision of its own; the native int bars are created with precision 0).
- Partial posts carry the running σ of the bar so far, like `Mean`.
- `null` = **unknown**, never the same as 0: emitted for a bar that took a pre-aggregated partial
  (`AddPartial` carries no spread, so the whole bar's σ becomes unknown) and absent from older collectors
  and third-party senders. The server stores and shows it as unknown
  ([`server/bar-stddev/feature.md`](../../server/bar-stddev/feature.md)).
- Compatibility (wire only): older servers ignore the unknown property (System.Text.Json default); a
  newer server accepts bars without it. The server's **stored** bar rows are not backward compatible —
  a pre-#1509 server cannot read rows written after the upgrade
  ([`server/bar-stddev/feature.md`](../../server/bar-stddev/feature.md#storage--persistence)). Pinned by `WireFormatGoldenLockTests` ↔ `NativeWireBarJsonMatchesNetByteLayout`
  and the `stddev` conformance cases in `bar_double_contract` / `bar_int_contract` / `bar_partial_contract` /
  `bar_sampled_partial_contract`.

## Registration / command DTOs

`AddOrUpdateSensorRequest` (`SensorRequests/AddOrUpdateSensor/`): `path`, `sensorType?`, `description`, `keepHistory?`/`selfDestroy?`/TTLs (ticks), `statistics?`, `isSingletonSensor?`, `aggregateData?`, `enableGrafana?`, `originalUnit?`, `displayUnit?`, `isForceUpdate`, `enumOptions` (`EnumOption { key:int, value:string, description:string, color:int(ARGB) }`), `alerts` / `ttlAlerts` (`AlertUpdateRequest { conditions[{combination, operation, property, target{type,value}}], status, template, icon, isDisabled, confirmationPeriod?, scheduledNotificationTime?, scheduledRepeatMode?, scheduledInstantSend? }`), `defaultAlertsOptions`. Obsolete compat properties: `TTL`, `TtlAlert`, `DefaultChats`.

History DTOs (`HistoryRequests/`): `HistoryRequest { path, from, to?, count?, options }`, `FileHistoryRequest { +fileName="temp", extension="csv", isZipArchive }` — used by server tooling; the collector itself does not query history.

## JSON conventions

What the .NET collector actually emits (`Client/HttpsClient/RequestHandlers/HttpRequest.cs` — default System.Text.Json options + `AllowNamedFloatingPointLiterals` + polymorphic `JsonRequestConverter`, NO naming policy, NO ignore conditions):

- **PascalCase** property names (`"Path"`, `"Comment"`, `"OpenTime"` — exactly as declared in C#).
- **Nulls and defaults ARE emitted** (`"Comment":null`, `"Status":1`). The `[DefaultValue]` attributes on DTOs are Newtonsoft-era leftovers and have no effect under System.Text.Json. The server deserializes case-insensitively and tolerates omissions — but a byte-compatible port must match what the .NET collector sends, not what the server minimally accepts.
- Enums serialized as **numbers**.
- **String escaping = System.Text.Json's DEFAULT `JavaScriptEncoder`** (no custom encoder is set). This escapes more than the JSON minimum: `<` `>` `&` `'` `+` `` ` `` and **every non-ASCII** code point become `\uXXXX` (UPPERCASE hex, surrogate pairs for astral planes), and the double quote is `"` — **not** `\"`. Only `\` `\b` `\t` `\n` `\f` `\r` use short escapes. A byte-compatible port must reproduce this exactly (`EscapeJsonWire` in the native port), not a naive `\"`-style escaper; an all-ASCII test corpus will not catch the difference.
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
- `ProductEntity.Policies` (non-TTL persisted policy-id list on a product) is deprecated and no longer populated by the server. Node-level alerts on Folders/Products were removed in #1142; the collector wire format is unaffected because template materialization targets sensors only.
- **Batch storage semantics (#1441):** values are stored regardless of their order inside a batch. An out-of-order value (older than the sensor's cached newest) is persisted directly, subject to a retention floor: a value older than the sensor's KeepHistory window (when configured — the retention pass purges such rows anyway) or than the last history clear is NOT stored (counted as `OutOfRetentionValues`). The clear half of the floor is stamped by BOTH clear paths — explicit operator clears and the automatic KeepHistory retention pass (monotone max), so widening KeepHistory does not lower it until restart: backfill into the newly opened older window is conservatively refused (counted, rate-limited warn) for the current uptime. The floor is bounded ONLY by KeepHistory and clears: with Forever retention (KeepHistory = None/never configured, never cleared) the floor is unset, so such sensors accept arbitrary past timestamps and the weekly-database protection does not apply to them. The floor deliberately does not use the oldest stored row as a boundary: backfill older than the first stored row but inside the retention window IS stored — whether data survives must not depend on the server's restart history. An out-of-order instant value is otherwise identical to before the change — validated, its alerts/notifications evaluated, `ReceivedNewValue` delivered to charts and live views — it just never becomes the cached newest value. The floor check runs at the persistence step, AFTER validation and live delivery: a value refused by the floor may therefore have already fired alerts and been delivered to live views while never appearing in history (the same was true of any out-of-order value before #1441 — the refusal only skips the database write). With `AggregateValues` on, an older value with EQUAL content folds into the cached newest (no row of its own, pre-#1441 semantics); a non-foldable older value whose tick lands ON or INSIDE an existing aggregated span row is NOT stored (counted as `AggregateSpanOverlapsSkipped`) — a direct write there would replace the whole stored run (the DB key is the span's first tick) or add a row overlapping the span; other non-foldable older values take the direct write. **Bar sensors never take the direct write**: same-period partials merge in memory by `OpenTime`, and a bar row's DB key is its SEND time — persisting a late partial directly would duplicate the period when the completed bar lands. Within one timestamp the LAST WRITTEN value keeps the row (the storage key is `(sensorId, ticks)`, a compatibility-frozen format): a value landing on the cached newest's tick supersedes the row just written for it, and a non-aggregate sensor's out-of-order write at a tick that already has a row overwrites that older row (not counted — detecting it would need a read-before-write on the hot path; aggregate sensors DO pay that read on their cold out-of-order path, see `AggregateSpanOverlapsSkipped`). Values rejected by the singleton gate (or a type mismatch) are counted but not stored; the batch response stays empty for them (a rejected value is a sensor-level event, not a transport error). A 200 response therefore means "transport accepted", not "every value stored". The visibility surface is the `hsm-server` log plus internal per-sensor diagnostics — `OutOfOrderValuesStored`, `SameTickValuesSuperseded`, `RejectedValues`, `OutOfRetentionValues`, `AggregateSpanOverlapsSkipped` — which are in-memory only (reset on restart, not yet surfaced through API/UI); the out-of-order, supersede, out-of-retention and aggregate-span Warns are rate-limited to the first occurrence and every 1000th (carrying a running total), and rejections are counted without per-value logging (singleton rejections are steady state).

## Native port (C++)

The native collector (`src/native/collector`, #1096) reproduces this wire **byte-for-byte** against the **net8 / Core** `System.Text.Json` output (the shortest-double runtime; net472 doubles diverge and are out of scope, as in `number_format_contract`). `BuildWire{Value,Bar,File,Registration}Json` in `hsm_collector.cpp` emit the exact property order (most-derived-first, base-last, `Type` first), `Key:null`, ISO-8601-Z time (fraction trimmed), TimeSpan ".NET c", `List<byte>` numeric array, `StdDev` after `Mean` (`null` when unknown), `Percentiles:null`, and the full `AddOrUpdateSensorRequest` shape. String escaping goes through a dedicated `EscapeJsonWire` that mirrors the default `JavaScriptEncoder` (the internal-conformance `EscapeJson` keeps its own simpler `\"` convention and must not be confused with it). Parity is locked from both sides: native `native_wire_*` unit tests pin the exact bytes (including double/bool/double-bar, the `<>&'+"`/non-ASCII escaping cases, the int-bar half-to-even mean, and pre-epoch / `Int64.MinValue` time edges), and `WireFormatGoldenLockTests` (net8 IntegrationTests) asserts the **same** strings against the real `HttpRequest<T>` serializer — if .NET drifts, that test fails first and both sides update in lockstep.

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMSensorDataObjects/**` | DTOs + enums (source of truth) |
| `src/collector/HSMDataCollector/Client/HttpsClient/Endpoints.cs` | Route constants |
| `src/collector/HSMDataCollector/Converters/*.cs` | Serializer configuration |
| `ai-docs/Wiki/REST-API.md` | Human-facing examples |

## Dependencies

- Used by: collector `data-pipeline`/`http-client`, server controllers, `src/wrapper`, native C++ port.
