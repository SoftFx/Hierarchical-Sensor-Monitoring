# Feature: Sensor API Wire Contract

> Owner: shared | Last reviewed: 2026-11-10 | Canonical: yes
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
| `IntBarSensorValue` / `DoubleBarSensorValue` | `StdDev?/Min/Max/Mean/Count/FirstValue?/LastValue/OpenTime/CloseTime` (+obsolete `Percentiles` — never populated, but serialized as `null` since nulls are not omitted). `StdDev` (#1509) is declared on these two DTOs only and sent by the native collector only: see below |
| `FileSensorValue` | `Value` = `List<byte>` → **numeric JSON array** (`[72,105,...]`, NOT base64 — System.Text.Json base64-encodes `byte[]` but not `List<byte>`), `Name`, `Extension` |

There is no Counter DTO: `CounterSensorValue.cs` is a legacy file name that contains `RateSensorValue`.

### Bar StdDev (#1509)

**Compatible-additive wire change, sent by the native collector only** (DTO `HSMSensorDataObjects`
3.2.0, native collector 0.11.0). By owner decision 2026-10-07 the managed .NET collector does **not**
send it (#1529, [ADR-0009](../../../../docs/decisions/0009-bar-stddev-native-only.md) — an explicit
exception to rules #9/#10). The server-facing DTOs `IntBarSensorValue` / `DoubleBarSensorValue` gained
`StdDev` (`double?`); System.Text.Json serializes a property declared on the concrete DTO right after
`Type`, and the native wire puts it there:

```
native:  {"Type":4,"StdDev":1.41,"Min":1,"Max":5,"Mean":3,"FirstValue":1,"LastValue":5,"Percentiles":null,...}
managed: {"Type":4,"Min":1,"Max":5,"Mean":3,"FirstValue":1,"LastValue":5,"Percentiles":null,...}
```

The managed collector's bar types (`IntMonitoringBar` / `DoubleMonitoringBar`) derive from
`BarSensorValueBase<T>`, which does not carry the field, so their bytes are unchanged; the two wires
differ by exactly that one member (pinned by `WireFormatGoldenLockTests` ↔ `NativeWireBarJsonMatchesNetByteLayout`).

- Meaning: the **population** standard deviation of the bar's samples, `sqrt(Σ(x − mean)² / Count)`;
  `0` for a single sample. A double on **both** bar flavors (an int bar posts `1.41`, not `1`).
- Accumulation (native; the reference for a future managed port, #1529): Welford, per sample before
  `Count` is incremented — `n = Count + 1; delta = x − m; m += delta / n; M2 += delta * (x − m)` — and on
  post `σ = Count <= 1 ? 0 : sqrt(M2 / Count)`. O(1) per sample, no sample storage, no cancellation for a
  large offset with a small spread (a Σx/Σx² form would lose it). The running `m` is separate from the
  sum behind the wire `Mean`, which is unchanged. The native build disables floating-point contraction
  (`-ffp-contract=off` on GCC/Clang) so no compiler fuses `delta * (x − m) + M2` into an FMA; RyuJIT never
  does, so a managed port with the same operation order would produce the same bytes.
- Rounding: double bar — the bar precision, half away from zero (as `Mean`, the .NET
  `Math.Round(σ, Precision, AwayFromZero)` contract); int bar — a fixed 2 digits, half away from zero (an
  int bar has no precision of its own).
- Partial posts carry the running σ of the bar so far, like `Mean`.
- `null` / absent = **unknown**, never the same as 0: `null` for a bar that took a pre-aggregated partial
  (`AddPartial` carries no spread, so the whole bar's σ becomes unknown) and for a spread too large for
  a double (M2 overflows for samples around 1e154 and up; σ is never sent as a non-finite number);
  absent from the managed
  collector, older native collectors and other senders. The server stores and shows it as unknown
  ([`server/bar-stddev/feature.md`](../../server/bar-stddev/feature.md)).
- The server stores only a finite, non-negative σ: anything else a sender posts (negative, NaN,
  infinity) is normalised to unknown at conversion (`BarStdDev.Normalize`).
- Compatibility (wire only): older servers ignore the unknown property (System.Text.Json default); a
  newer server accepts bars without it. The server's **stored** bar rows are not backward compatible —
  a pre-#1509 server cannot read rows written after the upgrade
  ([`server/bar-stddev/feature.md`](../../server/bar-stddev/feature.md#storage--persistence)).
- Conformance: the native-only fixture `tests/conformance/collector/native/bar_stddev_contract.hsmtest`
  (the managed driver marks `expect_bar_field … stddev` `CONFORMANCE-UNSUPPORTED` (#1529) and never
  discovers that folder); the differential fuzzer strips the native-only canonical `StdDev` field.

## Registration / command DTOs

`AddOrUpdateSensorRequest` (`SensorRequests/AddOrUpdateSensor/`): `path`, `sensorType?`, `description`, `keepHistory?`/`selfDestroy?`/TTLs (ticks), `statistics?`, `isSingletonSensor?`, `aggregateData?`, `enableGrafana?`, `originalUnit?`, `displayUnit?`, `isForceUpdate`, `enumOptions` (`EnumOption { key:int, value:string, description:string, color:int(ARGB) }`), `alerts` / `ttlAlerts` (`AlertUpdateRequest { conditions[{combination, operation, property, target{type,value}}], status, template, icon, isDisabled, confirmationPeriod?, scheduledNotificationTime?, scheduledRepeatMode?, scheduledInstantSend? }`), `defaultAlertsOptions`, `previousPaths` (`string[]?`, #1724 — full product-relative paths the sensor was registered under before; the server moves the sensor found there to `path`, merging into one already there, see `features/server/sensor-move/feature.md`). Obsolete compat properties: `TTL`, `TtlAlert`, `DefaultChats`.

`previousPaths` is the one member **omitted when unused** instead of serialized as `null`: both collectors leave it out of the wire when the list is null/empty (managed: `JsonRequestConverter` drops a null `PreviousPaths`; native: `BuildPreviousPathsMember` renders nothing), so a registration without previous paths is byte-identical to the pre-#1724 wire. When present it sits right after `EnumOptions` (the DTO declaration order). An older server ignores the member (System.Text.Json skips unknown properties), so a new collector against an old server simply registers the new path.

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
| `commands` | command batch (response: error dictionary keyed by sensor path; statuses below) |
| `addOrUpdate` | `AddOrUpdateSensorRequest` (statuses below) |
| `testConnection` | — (response: 200 with header `X-Hsm-Server: 1`, which identifies an HSM server; the HSM Agent's retired-port check relies on it, #1655) |
| `session` (GET, WebSocket upgrade) | collector session, see below |

### Statuses of the registration routes (#1749)

Sensor registrations travel on `commands` (a batch; what both collectors send) and `addOrUpdate` (a single registration; the managed collector when it does not batch). A collector decides by the status alone what to do with the package it sent:

- **2xx**: delivered. Errors in the body are logged per path; nothing is sent again.
- **4xx**: a final refusal. The package is reported once and **not sent again** (native since 0.10.3, managed since 3.6.2): the same request and key would be refused on every repeat. The sensors of that package stay without description, unit, TTL and alerts until the host starts again.
- **5xx, or no answer**: the same package is sent again on the next cycle, with no limit on the number of repeats.

So the server answers 5xx only for a failure of its own that may pass, and never for a request that fails the same way on every repeat.

| What happened | `commands` | `addOrUpdate` | What a collector does |
|---|---|---|---|
| Applied | `200`, `{}` | `200`, no body | delivered |
| The server refuses a registration: the key it carries in its body (no `Key` header) is unknown, expired or blocked, or the server cannot convert it (an enum value this server does not know, such as a newer sensor type, unit or alert operation; more TTLs than TTL alerts; an alert or an alert condition that is `null`; on `addOrUpdate` also no `Path`) | `200`, the reason under the registration's path; the other commands of the batch are applied | `406`, the reason as text | `commands`: delivered, the reason is logged for that path. `addOrUpdate`: final |
| The key in the `Key` header is rejected or may not send data | `406`, the reason as text | `406`, the reason as text | final |
| The body cannot be read: not JSON, not a list, a command `Type` the server does not know, a member of the wrong kind, no body; on `commands` also a list with a `null` element or with a command without a `Path` | `400` (`application/problem+json`, with a text that says what is wrong, e.g. `The command at index 1 has no path.`), nothing is applied, the other commands of the list included | `400` (`application/problem+json`), nothing is applied | final |
| An exception while the server applies a command: the server's own fault | `500`: some commands faulted, **the others were applied**; send the package again. The body is the dictionary of per-path errors of the commands the server refused; a faulted command has no entry in it | `500`; the body is the text `Failed to update sensor! Update request: {…}` | sent again on the next cycle |

- **A `500` on `commands` means "some commands faulted, the others were applied; send the package again".** A fault on one command does not end the batch: the commands before and after it are applied, and refused as usual, and the status is `500` if at least one command faulted. A collector sends a package answered 5xx again every cycle, and the managed collector keeps it ahead of the commands it issues later (#1750); had the server stopped at the fault, one command that faults on every repeat would keep every later registration of that collector from being applied. The resend applies the whole package again from its first command, which is what a collector restart does. The server logs one `Error` line for the request with the number of commands that faulted and the first exception, not a line per command.
- **A fault changed its status, not its body.** Before #1749 a fault was a `400` with the same kind of body, so a fault that would have passed cost a collector the whole package. Nothing a client read is gone.
- **A repeated package is ordinary**: a collector registers all its sensors again at every start, and the server applies a package from its first command each time.
- Two more things changed with #1749, both to keep `500` for the server's own faults. A registration the server cannot convert used to throw, which ended the whole batch with a `400`; it is now refused on its own (the row above), since it fails the same way on every repeat. A `commands` body the server cannot read used to end as a `500` (the model binder threw); it is now a `400`, as on the collector session.
- **A refusal is only what the request causes.** The server refuses a registration as unconvertible for exactly what its converters throw for a request (`NotImplementedException` for an unknown enum value, `ArgumentException` for more TTLs than TTL alerts) and for the parts it checks itself (no path, a `null` alert or alert condition). What the server knows about the caller (the key and its product) is read outside that, so a failure of the server's state is a `500`, never a refusal.
- **A `commands` list with a `null` element or with a command without a `Path` is refused as a whole** (`400`, the row above), before anything is applied: there is no path to report such a command under, and it fails the same way on every repeat.
- **Not covered: what the sensor cache catches itself never reaches the route as an exception.** A registration the cache refuses (a path that is too long, a sensor that exists with another type) or fails to store is logged by the server and answered as applied: `200` with no entry for the path, or with the exception's text under the path when the failure escaped the cache's own handling into its update queue. A collector takes both as delivered. Tracked in #1756.
- **Not covered: a request with no key at all** (neither the `Key` header nor a key in the body), which no collector sends, still throws inside the route and is therefore answered `500`, although the request is at fault. Also in #1756.

Server tests: `src/tests/HSMServer.Core.Tests/SensorApi/RegistrationStatusTests.cs`.

## Collector session (WebSocket)

A second transport for collectors, beside the HTTP routes above (#1664, epic #1660, ADR-0017). The HTTP routes are not retired (owner decision of 2026-11-11: script clients such as PowerShell use them); a collector uses one transport or the other, with no fallback between them. One WebSocket per collector at `GET {base}session`, on the same origin and with the same `Key` / `ClientName` headers as the HTTP routes. The bodies are the same. **Both collectors can use it**, off by default: the native collector since 0.12.0 (#1668: a setting of the agent and of the Linux probe), the managed collector since 3.9.0 (`CollectorOptions.Transport = Session`). Until the session can carry everything (see request ops below) the HTTP routes are the only complete transport.

A collector either uses the session or the HTTP routes, by its own setting; the server does not negotiate it. Nothing in the session refers to HTTP: there is no fallback to it, no announcement of sessions in HTTP answers, and no side traffic over it.

**Handshake.** `Sec-WebSocket-Protocol: hsm.sensors.v1` is required and is echoed. Optional request headers state facts about the collector: `X-Collector-Kind` (`managed` / `native`), `X-Collector-Version`, `X-Agent-Version`. A refused upgrade is an HTTP status. The key is checked first, so `406` (no usable access key) can come back whatever else is true. Then: `404` sessions are off, `400` not an upgrade or no known subprotocol, `405` a method other than `GET` (or `CONNECT` over HTTP/2), `406` a key that exists but is blocked, expired or may not send, `429` this client address already holds as many sessions as the server allows one address, `503` the session cap is reached or the server is shutting down. `503` and `429` pass; the others do not until someone changes a setting, a key or the collector. Either way a refused collector keeps its data and tries again; it does not switch to the HTTP routes.

**Frames.** Text frames, one JSON object each. The field names are lower-case and case-sensitive.

| Direction | Fields | Meaning |
|---|---|---|
| collector to server | `id`, `op`, `body` | a request; `id` is an integer chosen by the collector |
| server to collector | `ack`, `status`, then `body` or `error` | the answer to request `ack` |
| server to collector | `op`, `body` | a push; no `id`, no answer expected |
| either | `op` = `ping` / `pong` | heartbeat; no `id` |

```json
{"op":"hello","body":{"maxFrameBytes":4194304,"heartbeatSeconds":30}}

{"id":17,"op":"list","body":[{"Type":1,"Value":42,"Comment":null,"Time":"2026-11-10T10:00:00Z","Status":1,"Key":null,"Path":"p/int"}]}
{"ack":17,"status":200,"body":{}}

{"id":18,"op":"commands","body":[{"Type":0,"Path":"p/int","SensorType":1}]}
{"ack":18,"status":200,"body":{"p/int":"error text for this sensor"}}

{"id":19,"op":"list","body":[]}
{"ack":19,"status":400,"error":"empty batch"}

{"op":"ping"}
{"op":"pong"}
```

- **`hello`** is the first frame the server sends. `maxFrameBytes` is the largest frame it accepts; `heartbeatSeconds` is the heartbeat interval.
- **Request ops**: `list` and `commands`. The `body` is **exactly the JSON the collector would have sent as the HTTP body of the route of the same name**, read by the server with the same options. The typed single-value routes have no op and need none, as both collectors batch. **`file` has no op yet.** A collector must split a batch so that every frame fits `maxFrameBytes` (a requirement on the collectors that use the session; both collectors split `list` and `commands` batches), and an element that cannot fit on its own has no way through: the managed collector drops such a value, reports it and counts it as lost, and refuses a file value before it is queued. Files, such elements and directive push (directives still travel only as headers on HTTP answers) have to be solved before a collector can do everything on a session.
- **`status` is the HTTP status the same request would have got** on that route, with one exception: a `list` body the server cannot parse is `400` (`"malformed body"`), where the HTTP route ends as a `500`. A `commands` body the server cannot parse is `400` on both (#1749), and so is a `commands` list with a `null` element or with a command without a `Path` (`"malformed body"` on the session): nothing of it is applied.
- **A success ack** carries the route's success body in `body`: the dictionary of per-path errors, empty when everything was accepted. Its keys are sensor paths exactly as sent.
- **A failure ack** carries a short `error` text and no `body`. It never echoes the request, unlike the HTTP `400` of `list`, and it does not carry the per-path errors the HTTP `500` of `commands` does. Texts in version 1: `empty batch`, `malformed body`, `unknown op`, `failed to store the batch`, `failed to apply the commands`. They are for logs; a collector decides by `status`.
- **A fault of the server while it applies a `commands` request is a `500` ack** (`"failed to apply the commands"`), as on the HTTP route (#1749; a `400` before): a collector takes a 4xx on a registration as final and sends a 5xx again. It means what the HTTP `500` means: some commands faulted, the others were applied, send the frame again. The refusals of the other commands do not travel in it (a failure ack has no `body`); a resend that does not fault carries them in its `200`. See "Statuses of the registration routes" above; the rows of that table hold for the `commands` op too. A fault while storing a `list` batch stays a `400` (`"failed to store the batch"`) on both transports: a collector sends a value batch again whatever the failure.
- **Heartbeat.** A collector that has sent nothing for `heartbeatSeconds` sends `{"op":"ping"}`; the server answers `{"op":"pong"}`. The server drops a session after three intervals without any inbound frame. WebSocket ping/pong frames are not the heartbeat. A heartbeat is recognised by its `op`; one that carries an `id` is answered the same way, with a plain `pong` and no ack. A ping is also where the server notices that the access key can no longer send (`1008`).
- **Shutdown.** The server closes with `1001`. A request it was still handling is acknowledged before the close, if handling it takes no more than a few seconds; a request that arrives after the close went out is not stored and gets no ack, so the collector sends it again on its next session.
- **Growth without a new version.** Unknown envelope fields are ignored by both sides. An unknown request `op` is answered with a `400` ack (`"unknown op"`); an unknown push `op` is ignored by the collector.
- **Close codes from the server**: `1001` shutdown, `1002` a request frame without an `id`, `1003` a binary frame, `1007` a frame that is not a JSON object, `1008` the access key can no longer send, `1009` a frame over `maxFrameBytes`.
- **Delivery is at-least-once**, as over HTTP: a batch whose ack was lost may have been stored.
- **What the collector expects of the server's frames.** A collector waits for an ack as long as it waits for an HTTP response (`RequestTimeout`, 30 s by default) and then closes the session, so a server that takes longer than that to store a batch loses the session as it would lose the HTTP request. The managed collector accepts frames of up to 8 MiB from the server (the native bound) and ends the session on a larger one.

Server behaviour, settings and tests: `aicontext/features/server/collector-session/feature.md`.

## Invariants

- Never renumber or reuse enum values; never rename JSON fields; additive evolution only.
- Server tolerates unknown/omitted optional fields; collectors must tolerate unknown response fields.
- `ProductEntity.Policies` (non-TTL persisted policy-id list on a product) is deprecated and no longer populated by the server. Node-level alerts on Folders/Products were removed in #1142; the collector wire format is unaffected because template materialization targets sensors only.
- **Batch storage semantics (#1441):** values are stored regardless of their order inside a batch. An out-of-order value (older than the sensor's cached newest) is persisted directly, subject to a retention floor: a value older than the sensor's KeepHistory window (when configured — the retention pass purges such rows anyway) or than the last history clear is NOT stored (counted as `OutOfRetentionValues`). The clear half of the floor is stamped by BOTH clear paths — explicit operator clears and the automatic KeepHistory retention pass (monotone max), so widening KeepHistory does not lower it until restart: backfill into the newly opened older window is conservatively refused (counted, rate-limited warn) for the current uptime. The floor is bounded ONLY by KeepHistory and clears: with Forever retention (KeepHistory = None/never configured, never cleared) the floor is unset, so such sensors accept arbitrary past timestamps and the weekly-database protection does not apply to them. The floor deliberately does not use the oldest stored row as a boundary: backfill older than the first stored row but inside the retention window IS stored — whether data survives must not depend on the server's restart history. An out-of-order instant value is otherwise identical to before the change — validated, its alerts/notifications evaluated, `ReceivedNewValue` delivered to charts and live views — it just never becomes the cached newest value. The floor check runs at the persistence step, AFTER validation and live delivery: a value refused by the floor may therefore have already fired alerts and been delivered to live views while never appearing in history (the same was true of any out-of-order value before #1441 — the refusal only skips the database write). With `AggregateValues` on, an older value with EQUAL content folds into the cached newest (no row of its own, pre-#1441 semantics); a non-foldable older value whose tick lands ON or INSIDE an existing aggregated span row is NOT stored (counted as `AggregateSpanOverlapsSkipped`) — a direct write there would replace the whole stored run (the DB key is the span's first tick) or add a row overlapping the span; other non-foldable older values take the direct write. **Bar sensors never take the direct write**: same-period partials merge in memory by `OpenTime`, and a bar row's DB key is its SEND time — persisting a late partial directly would duplicate the period when the completed bar lands. Within one timestamp the LAST WRITTEN value keeps the row (the storage key is `(sensorId, ticks)`, a compatibility-frozen format): a value landing on the cached newest's tick supersedes the row just written for it, and a non-aggregate sensor's out-of-order write at a tick that already has a row overwrites that older row (not counted — detecting it would need a read-before-write on the hot path; aggregate sensors DO pay that read on their cold out-of-order path, see `AggregateSpanOverlapsSkipped`). Values rejected by the singleton gate (or a type mismatch) are counted but not stored; the batch response stays empty for them (a rejected value is a sensor-level event, not a transport error). A 200 response therefore means "transport accepted", not "every value stored". The visibility surface is the `hsm-server` log plus internal per-sensor diagnostics — `OutOfOrderValuesStored`, `SameTickValuesSuperseded`, `RejectedValues`, `OutOfRetentionValues`, `AggregateSpanOverlapsSkipped` — which are in-memory only (reset on restart, not yet surfaced through API/UI); the out-of-order, supersede, out-of-retention and aggregate-span Warns are rate-limited to the first occurrence and every 1000th (carrying a running total), and rejections are counted without per-value logging (singleton rejections are steady state).

## Native port (C++)

The native collector (`src/native/collector`, #1096) reproduces this wire **byte-for-byte** against the **net8 / Core** `System.Text.Json` output (the shortest-double runtime; net472 doubles diverge and are out of scope). Doubles follow the .NET Core layout — fixed notation for decimal exponents -4..16, scientific from 1e17 and below 1e-4 — since native 0.11.1; before that native switched to scientific from 1e15 (.NET Framework's `"R"` layout), so a value such as 1e15 went out as `1E+15` where STJ writes `1000000000000000` (#1528, pinned by `number_format_contract:double_wire_text_matrix`). `BuildWire{Value,Bar,File,Registration}Json` in `hsm_collector.cpp` emit the exact property order (most-derived-first, base-last, `Type` first), `Key:null`, ISO-8601-Z time (fraction trimmed), TimeSpan ".NET c", `List<byte>` numeric array, the native-only bar `StdDev` right after `Type` (`null` when unknown; #1509/#1529), `Percentiles:null`, and the full `AddOrUpdateSensorRequest` shape. String escaping goes through a dedicated `EscapeJsonWire` that mirrors the default `JavaScriptEncoder` (the internal-conformance `EscapeJson` keeps its own simpler `\"` convention and must not be confused with it). Parity is locked from both sides: native `native_wire_*` unit tests pin the exact bytes (including double/bool/double-bar, the `<>&'+"`/non-ASCII escaping cases, the int-bar half-to-even mean, and pre-epoch / `Int64.MinValue` time edges), and `WireFormatGoldenLockTests` (net8 IntegrationTests) asserts the **same** strings against the real `HttpRequest<T>` serializer — if .NET drifts, that test fails first and both sides update in lockstep.

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMSensorDataObjects/**` | DTOs + enums (source of truth) |
| `src/collector/HSMDataCollector/Client/HttpsClient/Endpoints.cs` | Route constants |
| `src/server/HSMServer/SensorApi/Sessions/SessionProtocol.cs` | Collector session: names, close codes, frame writers (server) |
| `src/collector/HSMDataCollector/Converters/*.cs` | Serializer configuration |
| `ai-docs/Wiki/REST-API.md` | Human-facing examples |

## Dependencies

- Used by: collector `data-pipeline`/`http-client`, server controllers, `src/wrapper`, native C++ port.
