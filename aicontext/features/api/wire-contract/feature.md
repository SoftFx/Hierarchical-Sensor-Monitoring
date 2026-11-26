# Feature: Sensor API Wire Contract

> Owner: shared | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: API - the frozen wire contract between collectors (any language) and HSMServer: enums, DTOs, JSON conventions, endpoints

---

## Overview

Everything in this file is **byte-for-byte compatibility-critical**: the .NET collector, the C++/CLI wrapper, the native C++ port, and the server must agree on these values. Source of truth: `src/api/HSMSensorDataObjects/`. Never renumber enums; add new members with new values only.

## Enums (numeric values are the contract)

`SensorType` (`SensorType.cs`): BooleanSensor=0, IntSensor=1, DoubleSensor=2, StringSensor=3, IntegerBarSensor=4, DoubleBarSensor=5, FileSensor=6, TimeSpanSensor=7, VersionSensor=8, RateSensor=9, EnumSensor=10, TableSensor=11 (#1845: registration only; a table sensor takes no values, see "Table sensor registration" below).

`TableColumnType` (`SensorRequests/AddOrUpdateSensor/TableColumnRequest.cs`, #1845): Time=1, Int64=2, Double=3, the numbers of the table block format.

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

`AddOrUpdateSensorRequest` (`SensorRequests/AddOrUpdateSensor/`): `path`, `sensorType?`, `description`, `keepHistory?`/`selfDestroy?`/TTLs (ticks), `statistics?`, `isSingletonSensor?`, `aggregateData?`, `enableGrafana?`, `originalUnit?`, `displayUnit?`, `isForceUpdate`, `enumOptions` (`EnumOption { key:int, value:string, description:string, color:int(ARGB) }`), `alerts` / `ttlAlerts` (`AlertUpdateRequest { conditions[{combination, operation, property, target{type,value}}], status, template, icon, isDisabled, confirmationPeriod?, scheduledNotificationTime?, scheduledRepeatMode?, scheduledInstantSend? }`), `defaultAlertsOptions`, `previousPaths` (`string[]?`, #1724 — full product-relative paths the sensor was registered under before; the server moves the sensor found there to `path`, merging into one already there, see `features/server/sensor-move/feature.md`), `tableColumns` (#1845, `List<TableColumnRequest>?`, the schema of a table sensor; see "Table sensor registration" below). Obsolete compat properties: `TTL`, `TtlAlert`, `DefaultChats`.

`previousPaths` is the one member **omitted when unused** instead of serialized as `null`: both collectors leave it out of the wire when the list is null/empty (managed: `JsonRequestConverter` drops a null `PreviousPaths`; native: `BuildPreviousPathsMember` renders nothing), so a registration without previous paths is byte-identical to the pre-#1724 wire. When present it sits right after `EnumOptions` (the DTO declaration order). An older server ignores the member (System.Text.Json skips unknown properties), so a new collector against an old server simply registers the new path.

### Table sensor registration (#1845)

A table sensor (ADR-0021, `aicontext/features/server/table-sensors/feature.md`) is registered like any sensor, on `commands` (HTTP or the session's `commands` op) or `addOrUpdate`, with `SensorType` 11 and its schema in `TableColumns` (`List<TableColumnRequest>`, `TableColumnRequest { Name: string, Type: TableColumnType }`): the columns in order, the first of which is the time. The schema id is never sent: both sides compute it from the columns (`aicontext/features/api/table-block/feature.md`, "Table schema").

```
{"Type":0,"Alerts":null,"TtlAlerts":null,"TtlAlert":null,"SensorType":11,"Description":null,"DefaultChats":null,"KeepHistory":null,"SelfDestroy":null,"TTLs":null,"TTL":null,"Statistics":null,"IsSingletonSensor":null,"AggregateData":null,"EnableGrafana":null,"OriginalUnit":null,"DisplayUnit":null,"DefaultAlertsOptions":0,"IsForceUpdate":false,"EnumOptions":null,"TableColumns":[{"Name":"time","Type":1},{"Name":"bid","Type":3},{"Name":"volume","Type":2}],"Key":null,"Path":"quotes/EURUSD"}
```

- **`TableColumns` is omitted when unused**, like `previousPaths`: the managed collector's `JsonRequestConverter` drops a null member, so every registration without it is byte-identical to the wire before #1845 (pinned by the unchanged goldens of `WireFormatGoldenLockTests`; the table golden is `Registration_of_a_table_sensor_carries_its_columns_after_previous_paths`). When present it sits right after `PreviousPaths` (the DTO declaration order). The native collector writes the same bytes since 0.15.1 (#1848 part 4; pinned by `native_table_block_after_hello_and_probe`).
- **`KeepHistory`** is the table sensor's retention, as for any sensor (owner decision of 2026-11-24): stored with the sensor; it is applied in whole weeks by #1850. The other settings a table sensor cannot take (alerts, TTL alerts, aggregation) are refused (table-sensors feature, "Registration rules").
- **Refusals** (answered as every refusal of a registration: an error under the path in a `200` on `commands` and in the session's ack, `406` with the text on `addOrUpdate`; never a fault), each text starting `The table sensor registration is refused: `:
  - `TableColumns` on a registration that is not of type 11 (also one without a type): `only a registration of a table sensor (SensorType 11) carries table columns`;
  - a schema that breaks the rules: `its table schema is invalid: <the rule>`, e.g. no column, a first column that is not the time, a column type that is not 1, 2 or 3 (`column 1 has the unknown type 7`), a `null` column, a name that is empty, too long or used twice, more columns than `TableSensors.MaxColumnsPerTable` (64 by default);
  - **a path not in normalized form**: a block names its tables by the registered path byte for byte, and the server stores a path normalized (no leading separator, no empty segments, no spaces around segments), so a table sensor is registered only under that form: `a table sensor is registered under its normalized path "a/b", and blocks name it so; this registration names "/a/b"`. A collector sends the normalized path, in registrations and in blocks.
  - **a path longer in UTF-8 than `TableSensors.MaxTableIdLength`** (1,024 bytes by default): a block names its table by that path and the block format refuses a longer identifier, so no row could ever reach it: `blocks name a table sensor by its path in UTF-8, at most 1024 bytes, this one has 1025`. Bytes, not characters.
  - A registration refused for any of these reasons **moves nothing**: in a `commands` batch or a session batch the move pre-pass (`PreviousPaths`, #1724) knows them too.
  - type 11 without `TableColumns`, another schema on a path that holds a table sensor, a path held by a sensor of another kind: the cache's refusals (table-sensors feature).
- **Against an older server** (verified by running `TableRegistrationWireTests` before the server change): type 11 is a value its converter does not know, so the registration is refused under its path, `The registration cannot be converted: it carries a value this server does not know …` in a `200` (`406` on `addOrUpdate`), and the rest of the package is applied; `TableColumns` is an unknown member and is ignored. A server before #1749 answers a `commands` batch with such a registration `400` as a whole, which a collector takes as final for the whole package: **a collector sends its table registrations in a `commands` request of their own**, never together with the registrations of other sensors.
- An older collector never sends type 11 or `TableColumns`; nothing changes for it.

`TableHistoryRequest` (`HistoryRequests/TableHistoryRequest.cs`, #1845): `Path`, `From`, `To`, `MaxRows?`, `Format` (`TableHistoryFormat`: Json=0, Csv=1, Blocks=2, a number), `MaxBlockFormatVersion?`. See "Table history".

History DTOs (`HistoryRequests/`): `HistoryRequest { path, from, to?, count?, options }`, `FileHistoryRequest { +fileName="temp", extension="csv", isZipArchive }` — used by server tooling; the collector itself does not query history. A large file value in the answer (#1885) carries an additive `LargeFile` description and no content (`../overview.md`, "A large file value in the history"); the request DTOs are unchanged.

## JSON conventions

What the .NET collector actually emits (`Client/HttpsClient/RequestHandlers/HttpRequest.cs` — default System.Text.Json options + `AllowNamedFloatingPointLiterals` + polymorphic `JsonRequestConverter`, NO naming policy, NO ignore conditions):

- **PascalCase** property names (`"Path"`, `"Comment"`, `"OpenTime"` — exactly as declared in C#).
- **Nulls and defaults ARE emitted** (`"Comment":null`, `"Status":1`). The `[DefaultValue]` attributes on DTOs are Newtonsoft-era leftovers and have no effect under System.Text.Json. The server deserializes case-insensitively and tolerates omissions — but a byte-compatible port must match what the .NET collector sends, not what the server minimally accepts.
- Enums serialized as **numbers**.
- **String escaping = System.Text.Json's DEFAULT `JavaScriptEncoder`** (no custom encoder is set). This escapes more than the JSON minimum: `<` `>` `&` `'` `+` `` ` `` and **every non-ASCII** code point become `\uXXXX` (UPPERCASE hex, surrogate pairs for astral planes), and the double quote is `"` — **not** `\"`. Only `\` `\b` `\t` `\n` `\f` `\r` use short escapes. A byte-compatible port must reproduce this exactly (`EscapeJsonWire` in the native port), not a naive `\"`-style escaper; an all-ASCII test corpus will not catch the difference.
- DateTime: ISO 8601; `Time` defaults to `DateTime.UtcNow` → `Z` suffix.
- TimeSpan: .NET "c" format `[-][d.]hh:mm:ss[.fffffff]` (days prefix possible; 7-digit fraction omitted when zero).
- Version: `a.b[.c[.d]]`. The server reads the value with a hand-written parser (the typed `version` route and the `Type:8` element of a `list` batch share it): a version string or a `{Major,Minor,Build,Revision}` object. Omitted fields keep the defaults of the other typed routes (`Status`→`Ok`, `Time`→now); a field that is present but malformed — an unparseable version string above all — is a client error, never a 500: the typed route answers **400**, the session transport answers **400** `malformed body` (#1671).
- Polymorphic batch (`list` endpoint): items discriminated by the **numeric `Type` property** (`SensorType` value) — the server's converter scans for a property named `Type` (case-insensitive) and switches on its int value. There is no string discriminator.
- Ingestion text hygiene (#1709) applies to EVERY sensor type's comment and to the manual value/comment edit from the web UI's status modal (the cache runs the SAME `SanitizeText` gate — mask, sanitize, mask again — on a copy built by the model's `WithText`, so the caller's object keeps its original text), not only to string values: HTML tags are stripped (`HtmlSanitizerService` — `markdown` and `br` survive; one shared `SanitizeText` gate serves both converter copies — it masks on BOTH sides of the sanitizer: the first, raw pass catches a raw `<` inside a password (the sanitizer would read that `<` as an unclosed tag and drop everything after it, so the shape must mask before the sanitizer runs), and the second pass masks the sanitizer's OUTPUT, so an entity-encoded `@` (`&#64;`) or a tag splitting the URL from its `@` still masks; masking after the sanitizer cannot reintroduce markup, since it re-encodes markup characters but leaves `:`, `/` and `@` literal, and re-masking already-masked text is a no-op (`***@` carries no `:`)), and the credential part of a connection string (`scheme://user:pass@host`) is masked to `scheme://***@host` before anything is stored. A credential is `user:password@` INSIDE the authority; the host after it is never parsed, so replica-set host lists (`mongodb://u:p@h1:27017,h2:27017/db`), bracketed IPv6 hosts (`postgres://u:p@[::1]:5432/db`) and URLs embedded in JSON, parentheses or trailing punctuation (exception text in a comment) all mask. The authority ends at a HEURISTIC boundary set — the characters RFC 3986 excludes from an authority (`"`, `<`, `>`, `{`, `}`, `[`, `]`, `\`, `|`), plus the sub-delims `' ( ) , ;`, legal there but treated as boundaries because they commonly delimit JSON and prose — so a URL without a path does not own the rest of its word — a later JSON field or an email after a comma (`{"url":"http://example.com","owner":"admin@corp.com"}`, `https://example.com,contact:ops@corp.com`) stays intact instead of being read as the credential tail. The mask covers UNENCODED password characters too (a raw `@`, `/`, `?` or `#` — producers that build connection strings by concatenation): the separator is the last `@` of the authority. `&` is not an exception: it always travels entity-encoded (`&amp;`) through the sanitizer, and the scanner steps over the whole entity instead of ending the credential at its `;`. THREE limits remain: a password whose part before the raw `/` reads exactly as `host:port` (an all-digit tail) is indistinguishable from a genuine `scheme://host:port/path@…` URL and stays unmasked (`mongodb://backup:1234/56@mongo/db`); a boundary character RAW inside an unencoded password also ends the authority/credential zone, so such a password stays unmasked — this covers the RFC-excluded characters AND the sub-delims `' ( ) , ;`, so operators know exactly which password characters are unprotected (`mongodb://u:p;ss@host/db`; `mongodb://u:p&ss@host/db` masks because its `&` is entity-encoded, and a raw `<` masks because the pre-sanitize pass runs before the sanitizer) — treating those characters as password text would read the credential-free JSON/email tails pinned above as credentials and destroy them; and a password with BOTH a raw `@` and a raw `/` masks only PARTLY — the separator is the last `@` of the authority, so the tail past the `/` leaks (`mongodb://u:p@ss/x@host/db` → `mongodb://***@ss/x@host/db`) — preferring the credential zone's later `@` would also grab the `@` of a query-string email (`mongodb://u:p@h/db?email=a@b`) and destroy a credential-free value. The raw-`/`-style fallback (password running past the path delimiter) fires only when the authority itself is userinfo-shaped — EMPTY, bracketed (`[::1]`) and `host:port` authorities never take it (RFC 3986 allows an EMPTY port, `port = *DIGIT`, so `http://example.com:/a@b` counts as host:port too), so `http://[::1]/a:b@c`, `file:///C:/x@y`, `http://example.com:/a@b` and `http://host:8080/path?email=a@b` stay untouched. A `scheme://user@host` without a password and an empty password (`scheme://user:@host`) carry no secret and stay too. The scan is word-by-word and linear in the input length (bounded work per character), so an adversarial megabyte value cannot pin a request thread. The scheme and host stay readable for monitoring; an equality policy on a full connection string therefore no longer matches once the value is masked. Values already stored before the fix keep their secret, so an exposed credential must be rotated.
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
| `testConnection` | — ; the dual-purpose pre-flight, decided by the `Key` header: a KEYLESS call is the liveness probe the Docker healthcheck, docker-compose and CI rely on — a constant 200, nothing more (#1678 review: answering 406 there marked every deployed container unhealthy and kept Caddy from starting); a KEYED call is the auth check — an unusable key (expired/blocked, or without send permission) answers the key's own 406 reason (#1649/#1678), and only a usable send key answers 200. The 200s carry the header `X-Hsm-Server: 1`, which identifies an HSM server — the HSM Agent's retired-port check relies on it (#1655) |
| `session` (GET, WebSocket upgrade) | collector session, see below |
| `tableHistory` | `TableHistoryRequest`: the rows of a table sensor (#1845), see "Table history" below |

### Statuses of the registration routes (#1749)

Sensor registrations travel on `commands` (a batch; what both collectors send) and `addOrUpdate` (a single registration; the managed collector when it does not batch). A collector decides by the status alone what to do with the package it sent:

- **2xx**: delivered. Errors in the body are logged per path; nothing is sent again.
- **4xx**: a final refusal. The package is reported once and **not sent again** (native since 0.10.3, managed since 3.6.2): the same request and key would be refused on every repeat. The sensors of that package stay without description, unit, TTL and alerts until the host starts again.
- **5xx, or no answer**: the same package is sent again on the next cycle, with no limit on the number of repeats.

So the server answers 5xx only for a failure of its own that may pass, and never for a request that fails the same way on every repeat.

| What happened | `commands` | `addOrUpdate` | What a collector does |
|---|---|---|---|
| Applied | `200`, `{}` | `200`, no body | delivered |
| The server refuses a registration: the key it carries in its body (no `Key` header) is unknown, expired or blocked, or the server cannot convert it (an enum value this server does not know, such as a newer sensor type, unit or alert operation; more TTLs than TTL alerts; an alert or an alert condition that is `null`; a `Path` with no part in it, `""` or `"/"`; on `addOrUpdate` also no `Path`) | `200`, the reason under the registration's path; the other commands of the batch are applied | `406`, the reason as text | `commands`: delivered, the reason is logged for that path. `addOrUpdate`: final |
| The key in the `Key` header is rejected or may not send data; or the request has no key at all, neither the header nor a key in its body (#1756) | `406`, the reason as text (`An access key is required` for no key at all); nothing is applied | `406`, the reason as text | final |
| The body cannot be read: not JSON, not a list, a command `Type` the server does not know, a member of the wrong kind, no body; on `commands` also a list with a `null` element or with a command without a `Path` | `400` (`application/problem+json`, with a text that says what is wrong, e.g. `The command at index 1 has no path.`), nothing is applied, the other commands of the list included | `400` (`application/problem+json`), nothing is applied | final |
| The server's own fault: an exception while it applies a command, a fault of the sensor cache or a failed write to the store underneath it, a failure while it looks the access key up (#1756) | `500`: some commands faulted, **the others were applied**; send the package again. The body is the dictionary of per-path errors of the commands the server refused; a faulted command has no entry in it | `500`; the body is the text `Failed to update sensor! Update request: {…}` | sent again on the next cycle |

- **A `500` on `commands` means "some commands faulted, the others were applied; send the package again".** A fault on one command does not end the batch: the commands before and after it are applied, and refused as usual, and the status is `500` if at least one command faulted. A collector sends a package answered 5xx again every cycle, and the managed collector keeps it ahead of the commands it issues later (#1750); had the server stopped at the fault, one command that faults on every repeat would keep every later registration of that collector from being applied. The resend applies the whole package again from its first command, which is what a collector restart does. The server logs one `Error` line for the request with the number of commands that faulted and the first exception, not a line per command, and not a line per resend either (the decision below).
- **Recorded decision: a fault that repeats (review of #1749).** A fault the server does not get past (a bug of its own on one sensor, a store that stays down) makes a collector send the package again on every cycle, with no limit. What follows from it, and is accepted:
  - On every resend the server **applies the other commands of the package again**. That is safe: it is what a collector restart does. A `PreviousPaths` move is not made twice: `ApplyPreviousPathsAsync` is idempotent (a path that was moved is an alias afterwards, not a sensor, and is skipped; the key's permissions are asked only when there is something to move), and it never fails the registration.
  - **The faulting command stays undelivered** for as long as the fault lasts; its sensor keeps receiving values without the registration's settings.
  - **On the managed collector** a held package that is full keeps the registrations issued after it waiting, until the fault passes or overflow eviction makes room (`aicontext/features/collector/data-pipeline/feature.md`, "The cost"). The native collector queues sensors, not commands, and posts their current state on each cycle.
  - **One line of the server's log is bounded, the others are not.** The `Error` line the route or the session handler writes for a faulting request is written once per 5 minutes for the same fault (the type and the message of the request's first exception) of the same access key, and the next line says how many requests with that fault went unlogged in between (`… 19 more request(s) with the same fault were not logged since the previous line.`). At most 256 distinct faults are remembered; a forgotten one is simply logged again (`RepeatedFaultLog`, both transports and both registration routes share one). **The sensor cache and the update queue write their own `Error` lines for a registration that faulted, on every resend**: the handler's line with the error text, `An error was occurred by creating sensor`, `An error was occurred while processing request: …`, the queue's `Item processing error` / `Adding error`, and the telemetry line of a failed key lookup. They are as they were before #1756, when they were written once per collector start; a fault that repeats now repeats them every cycle of every collector it hits. That is a known cost of such a fault, not bounded here.
  - The alternative, a 4xx for a fault that repeats, was rejected: at the moment it happens a fault that will repeat cannot be told from one that will pass, and a 4xx loses the registration for good, which is what #1749 fixed.
- **A fault changed its status, not its body.** Before #1749 a fault was a `400` with the same kind of body, so a fault that would have passed cost a collector the whole package. Nothing a client read is gone.
- **A repeated package is ordinary**: a collector registers all its sensors again at every start, and the server applies a package from its first command each time.
- Two more things changed with #1749, both to keep `500` for the server's own faults. A registration the server cannot convert used to throw, which ended the whole batch with a `400`; it is now refused on its own (the row above), since it fails the same way on every repeat. A `commands` body the server cannot read used to end as a `500` (the model binder threw); it is now a `400`, as on the collector session.
- **A refusal is only what the request causes.** The server refuses a registration as unconvertible for one exception of its converters, `NotImplementedException`, which they throw for an unknown enum value and nothing else throws, and for what it checks itself before converting: no path, a `null` alert or alert condition, more TTLs than TTL alerts. `ArgumentException` is deliberately not caught: the runtime throws it and its subclasses for a null source or a bad index, so a bug of a converter would be answered as a final refusal and logged without a stack trace. What the server knows about the caller (the key and its product) is read outside that, so a failure of the server's state is a `500`, never a refusal.
- **A `commands` list with a `null` element or with a command without a `Path` is refused as a whole** (`400`, the row above), before anything is applied: there is no path to report such a command under, and it fails the same way on every repeat.
- **A fault of the sensor cache or of the store reaches the route as a fault (#1756).** Before it the cache handled its own failures and the route answered `200` (with nothing for the path, or with the exception's text under it), so a collector took the registration as delivered. The cache now sorts a failed registration in one place (`TreeValuesCache.AddOrUpdateSensorAsync`), and only a failure with a reason to pass on a repeat is a fault:
  - **fault, `500`**: an exception on the way to the handler or out of it, and a failed write to the store. That is: the product's update queue cannot be found or made because the product is gone (`GetProduct` fails in `ProcessRequestAsync`); the queue is closed, as while the server stops; the handler throws outside the creation and the update of the sensor; the write of the sensor's row to the store fails, when the sensor is created or when its settings are saved. The rest of the package is applied, as for any fault of one command. A failed write is seen whether the store throws or not: the storage engine (LevelDB, `EnvironmentDatabaseWorker`) catches and logs a write it failed on and returns, so the registration writes the row with the store's checked variant (`IDatabaseCore.AddSensorChecked` / `UpdateSensorChecked`), which reports the failure. A sensor whose row was not written when it was created is rolled back in memory, so it is not left there without a row, and the resend creates it. Every other caller of the store (a sensor created by its first value, the web UI, product updates, startup) writes with the void methods and is told nothing, as before: a sensor a first value creates stays in memory when its row was not written.
  - **answered as before** (logged by the server, `200` with nothing for the path): what the handler finds wrong once it runs. The product is not there when the sensor is to be created (`Can't find product` inside the creation); the sensor is not there when its settings are to be applied; an exception while the sensor is built or its settings are applied in memory (a subscriber of the cache that throws, for one). These are not known to pass, and a failure that repeats would be sent again by every collector on every cycle.
  - **refused**: a path with no part in it (`""`, `"/"`), which the cache fails on with an exception; it is the request's fault, so the server refuses it as unconvertible (`it has no path`).
  - An alert the sensor's type does not support is skipped with a warning in the server's log and the rest of the registration is applied; the collector is not told.
  - The other callers of the cache (value ingestion, the web UI) see what they saw before: the mark is a property of the result (`TaskResult.IsFault`) that only the Sensor API reads.
- **A failure while the server looks the access key up is a `500` on the two registration routes (#1756)**, not the `406` of a rejected key, which a collector takes as final. Every other route answers it `406` as before: a collector sends a value batch again whatever the status, and tries a session upgrade again too.
- **Not refused by the server, although a caller might expect it**: a registration under a path deeper than a value may have is applied (the sensor is created, and values for it are then refused as too deep); a registration of an existing sensor with another type updates its settings and leaves the type as it is.
- **A registration forwarded through an alias is answered by what came of it (#1756).** When the registered path is an alias of a sensor that was moved to another root product (`AliasResolution.Forwarded`), the registration is not applied on the queue of the product it came for: it is handed to the queue that owns the sensor. The answer waits for its end there and follows the same rule as a direct registration: an error of that queue (it is closed, its handler threw) and a failed write of the sensor's row are faults, `500`; applied is `200`, as before; a registration that was dropped on the way (the sensor or its product went away, the sensor kept moving between products) is logged and answered as applied, as before. It used to be answered as applied at once, before the other queue had stored anything. The queue handler that forwards does not wait for the other queue (two products forwarding to each other would deadlock); the wait is the request's, outside any queue. That wait has a limit of 20 s (`TreeValuesCache.ForwardedRegistrationWait`, below the 30 s a collector waits for an answer by default): a forwarded registration that is not answered within it, as when the other product's queue is disposed with the request unread because the product was removed or the server is stopping, is a fault, `500`, and the collector sends it again.

Server tests: `src/tests/HSMServer.Core.Tests/SensorApi/RegistrationStatusTests.cs`, and `RegistrationCacheFaultTests.cs` next to it for the faults of the cache, the store and the key lookup (a store that fails once, on both transports).

## Collector session (WebSocket)

A second transport for collectors, beside the HTTP routes above (#1664, epic #1660, ADR-0017). The HTTP routes are not retired (owner decision of 2026-11-11: script clients such as PowerShell use them); a collector uses one transport or the other, with no fallback between them. One WebSocket per collector at `GET {base}session`, on the same origin and with the same `Key` / `ClientName` headers as the HTTP routes. The bodies are the same. **Both collectors can use it**: the native collector since 0.12.0 (#1668: a setting of the agent and of the Linux probe), the managed collector since 3.9.0 (`CollectorOptions.Transport`). It is the default transport since ADR-0019 (managed collector 4.0.0, agent 0.6.0, probe 0.13.0); `http` stays selectable.

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
{"op":"hello","body":{"maxFrameBytes":4194304,"heartbeatSeconds":30,"sessionId":"20e46170c0a84f6e9b0d6a1f3c2e5b77"}}

{"id":17,"op":"list","body":[{"Type":1,"Value":42,"Comment":null,"Time":"2026-11-10T10:00:00Z","Status":1,"Key":null,"Path":"p/int"}]}
{"ack":17,"status":200,"body":{}}

{"id":18,"op":"commands","body":[{"Type":0,"Path":"p/int","SensorType":1}]}
{"ack":18,"status":200,"body":{"p/int":"error text for this sensor"}}

{"id":19,"op":"list","body":[]}
{"ack":19,"status":400,"error":"empty batch"}

{"op":"ping"}
{"op":"pong"}

{"op":"directive","body":"sensor-disable:network"}
```

- **`hello`** is the first frame the server sends. Its `sessionId` (32 hex digits, #1845) is what a table connection of the same collector names in `X-Hsm-Session` (below); a collector without table sensors ignores it.
- **`directive`** (#1660 step 5) is a push: one server directive, its `body` the text of one `X-Hsm-Directive` header line (`name:argument` or a bare name). The server pushes directives only to a session whose upgrade stated `X-Agent-Version`, as it puts the headers only on the answers to such a client. The set is the one the headers carry (`update-available:<staged>` when auto-update is on and the staged agent is newer, then `sensor-enable:` / `sensor-disable:` for `computer`, `system`, `disk`, `network`, `module`, `process`), in that order; it is pushed whole right after the `hello`, a directive again when it is not in the set of the previous check (every 15 s), and whole again every 5 minutes. A collector hands each directive, in order, to what takes a header directive (managed: the internal `ServerDirectives`; native: the host callback, as verb and payload split at the first `:`). A `directive` whose `body` is not a string, or is empty or whitespace only, carries nothing and is ignored by both collectors, on the header path as well (whitespace as .NET's `char.IsWhiteSpace` counts it). `maxFrameBytes` is the largest frame it accepts; `heartbeatSeconds` is the heartbeat interval.
- **Request ops**: `list` and `commands`. The `body` is **exactly the JSON the collector would have sent as the HTTP body of the route of the same name**, read by the server with the same options. The typed single-value routes have no op and need none, as both collectors batch. **A file value is an element of a `list` frame** (#1660 step 2b): the `/list` batch takes a `FileSensorValue` (`"Type":6`, `Value` the numeric array of `List<byte>`) as it takes any other value, and the server stores it as `/file` would. There is no `file` op. A collector must split a batch so that every frame fits `maxFrameBytes` (a requirement on the collectors that use the session; both collectors split `list` and `commands` batches), and an element that cannot fit on its own has no way through: both collectors drop such a value, a file included, report it and count it in `Queue overflow`. A byte of a file is up to four bytes of JSON, so the default limit of 4 MiB carries a file of about 1 MiB; larger files need `CollectorSessions.MaxFrameBytes` raised on the server (up to 64 MiB, enough for the 10 MiB the collectors accept by default). One thing has no way through a session: an element larger than any frame the server allows.
- **`status` is the HTTP status the same request would have got** on that route. A body the server cannot parse — an unknown, missing or non-numeric `Type` discriminator above all — is `400` on both transports for `list` (#1672): `"malformed body"` on the session, a `400` problem-details answer naming the position in the body on HTTP. A `commands` body the server cannot parse is `400` on both (#1749), and so is a `commands` list with a `null` element or with a command without a `Path` (`"malformed body"` on the session): nothing of it is applied.
- **A success ack** carries the route's success body in `body`: the dictionary of per-path errors, empty when everything was accepted. Its keys are sensor paths exactly as sent. A file value the server refuses before it reaches its sensor (a path too deep, no permission to write there) is such an entry, where the HTTP `/file` route answers `406`.
- **A failure ack** carries a short `error` text and no `body`. It never echoes the request, unlike the HTTP `400` of `list`, and it does not carry the per-path errors the HTTP `500` of `commands` does. Texts in version 1: `empty batch`, `malformed body`, `unknown op`, `failed to store the batch`, `failed to apply the commands`. They are for logs; a collector decides by `status`.
- **A fault of the server while it applies a `commands` request is a `500` ack** (`"failed to apply the commands"`), as on the HTTP route (#1749; a `400` before): a collector takes a 4xx on a registration as final and sends a 5xx again. It means what the HTTP `500` means: some commands faulted, the others were applied, send the frame again. The refusals of the other commands do not travel in it (a failure ack has no `body`); a resend that does not fault carries them in its `200`. See "Statuses of the registration routes" above; the rows of that table hold for the `commands` op too. A fault while storing a `list` batch stays a `400` (`"failed to store the batch"`) on both transports: a collector sends a value batch again whatever the failure.
- **Heartbeat.** A collector that has sent nothing for `heartbeatSeconds` sends `{"op":"ping"}`; the server answers `{"op":"pong"}`. The server drops a session after three intervals without any inbound frame. WebSocket ping/pong frames are not the heartbeat. A heartbeat is recognised by its `op`; one that carries an `id` is answered the same way, with a plain `pong` and no ack. A ping is also where the server notices that the access key can no longer send (`1008`).
- **Shutdown.** The server closes with `1001`. A request it was still handling is acknowledged before the close, if handling it takes no more than a few seconds; a request that arrives after the close went out is not stored and gets no ack, so the collector sends it again on its next session.
- **Growth without a new version.** Unknown envelope fields are ignored by both sides. An unknown request `op` is answered with a `400` ack (`"unknown op"`); an unknown push `op` is ignored by the collector.
- **Close codes from the server**: `1001` shutdown, `1002` a request frame without an `id`, `1003` a binary frame, `1007` a frame that is not a JSON object, `1008` the access key can no longer send, `1009` a frame over `maxFrameBytes`.
- **Delivery is at-least-once**, as over HTTP: a batch whose ack was lost may have been stored.
- **What the collector expects of the server's frames.** A collector waits for an ack as long as it waits for an HTTP response (`RequestTimeout`, 30 s by default) and then closes the session, so a server that takes longer than that to store a batch loses the session as it would lose the HTTP request. The managed collector accepts frames of up to 8 MiB from the server (the native bound) and ends the session on a larger one.

Server behaviour, settings and tests: `aicontext/features/server/collector-session/feature.md`.

### Table connection (#1845, ADR-0021 point 7)

A collector with table sensors sends their rows as blocks (`aicontext/features/api/table-block/feature.md`) on a **second WebSocket of the same session protocol**, used only for blocks, so that a large block never waits ahead of a value or a command. Table sensors need the session transport: there is no HTTP route for sending blocks.

**Handshake.** `GET {base}session` with `Sec-WebSocket-Protocol: hsm.tables.v1` (echoed), the same `Key` / `ClientName` (and optional `X-Collector-Kind` / `X-Collector-Version`) headers, and `X-Hsm-Session: <sessionId>`, the id from the `hello` of the collector's live ordinary session. A refused upgrade is an HTTP status, checked in this order: `406` no usable key (as for the session); `404` sessions off, or **table connections off** (`CollectorSessions.MaxTableFrameBytes = 0`: table ingest stops, sessions go on); `400` neither subprotocol offered; `503` shutting down; **`409` `X-Hsm-Session` is missing, malformed, or names no live session of the same key** (open the session first, then the table connection). An older server knows only `hsm.sensors.v1` and answers `400` before anything is upgraded.

- **The pair.** A table connection belongs to one live session of the same key. It takes **no slot**: neither `MaxSessions` nor `MaxSessionsPerAddress` counts it, so the limits count collectors and a server holds at most three times `MaxSessions` sockets (a session, its table connection and its file connection, #1884). A session has at most one table connection: **a new one replaces the old** (the old is closed with `1000`, reason `replaced by a new table connection of its session`, after the ack of a block it is storing). **When the session ends, its table connection is closed** with `1000`, reason `its session ended`, again after the ack of a block in progress. A collector reconnects the session first and the table connection after it.
- **Hello.** The server's first frame: `{"op":"hello","body":{"maxFrameBytes":4194304,"heartbeatSeconds":30,"blockLimits":{"maxDecompressedBytes":67108864,"maxTables":65536,"maxRows":8388608,"maxColumnsPerTable":64,"maxTableIdLength":1024}}}`. `maxFrameBytes` is the largest binary frame (`CollectorSessions.MaxTableFrameBytes`, 4 MiB by default; a real tick of a thousand tables is 25–50 KB), header included; `blockLimits` are the limits a block is checked against (section `TableSensors`). A collector splits a tick's rows into blocks that keep to both.
- **Text frames** are those of the session: `ping` / `pong` (the heartbeat and the silence limit are the session's), acks. A text request with any `op` is answered `400` `unknown op`: the table connection takes no `list` or `commands`. A text frame that is not JSON closes with `1007`, a request without `id` with `1002`, as on the session.
- **Binary frames** carry the requests. Version 1, little-endian, a 40-byte header and then the block:

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | frame version, `1` |
| 1 | 1 | kind: `1` block, `2` probe |
| 2 | 2 | reserved, `0` |
| 4 | 4 | header length (u32): where the block starts; `40` in version 1, the server accepts more and skips the rest |
| 8 | 8 | request id (i64), chosen by the collector, echoed as `ack` |
| 16 | 16 | producer id: 16 opaque bytes, read as a .NET `Guid` (`new Guid(bytes)`) and stored as they are; not all zero |
| 32 | 8 | producer sequence (i64, ≥ 0) |
| 40 | … | the block, to the end of the frame; nothing for a probe |

  A frame shorter than 40 bytes, of another version or kind, with reserved bytes set, or with a header length below 40 or past the frame, cannot be answered (its id is not trusted) and closes the connection with **`1002`**. A frame over `maxFrameBytes` closes with **`1009`**.
- **Probe** (kind 2): answered `{"ack":<id>,"status":200,"body":{}}`. A collector sends one after the hello to see that binary frames get through whatever is between it and the server; a path that drops them shows here, before any block waits on it.
- **Acknowledgement of a block**, a text frame; the collector decides by `status`:

```json
{"ack":17,"status":200,"body":{"outcome":"stored","tables":1000,"refusedCount":1,"refused":[{"index":3,"table":"quotes/XAUUSD","reason":"UnknownSensor"}]}}
{"ack":18,"status":200,"body":{"outcome":"duplicate","tables":1000,"refusedCount":0,"refused":[]}}
{"ack":18,"status":200,"body":{"outcome":"duplicate","tables":1000,"refusedKnown":false}}
{"ack":19,"status":200,"body":{"outcome":"allRefused","tables":2,"refusedCount":2,"refused":[{"index":0,"table":"a/b","reason":"SchemaIdMismatch"},{"index":1,"table":"a/c","reason":"SensorBlocked"}]}}
{"ack":20,"status":400,"error":"invalid block: ChecksumMismatch"}
{"ack":21,"status":503,"error":"the block store cannot take blocks now","retryAfter":60}
```

  - `200` settles the block. `stored`: the tables not in `refused` were taken. `duplicate`: the producer's block with this sequence was taken before, and the answer is that first one's. `allRefused`: every table was refused and nothing was stored; a final answer: the collector drops the block and does not send its producer sequence again (once a later block of the producer is merged, #1850, a resend at or below the producer's watermark is answered `duplicate` without being stored; rows sent after the sensors are registered come in later blocks). `refused` lists the refused tables by directory index, name (the identifier as text) and reason: `UnknownSensor`, `NotATableSensor`, `SensorBlocked`, `SensorCreatedAfterBlock`, `SchemaIdMismatch`, `ColumnTypesMismatch`, `InvalidIdentifier` (strings; a collector logs a reason it does not know as it is). The rows of a refused table are dropped and counted; a refused table stays refused in a resend.
  - **`refusedCount` is the exact number of refused tables; `refused` lists at most the first 100** of them, in directory order, each name cut to 256 characters (with `...`): a block of tens of thousands of unknown tables gets an ack of a few kilobytes, not of tens of megabytes. The block itself is judged on every table, and the server records every refused table with the stored block. A collector that needs the rest knows them by their indexes in its own block.
  - **`refused` may be absent, with `"refusedKnown":false`**, on a `duplicate` whose first put's refusals the server cannot tell (a stored envelope it cannot read, or a block whose hour the batch store has merged, #1850: its producer sequence is at or below the producer's watermark). A collector reads both forms; the rows were settled by the first put either way.
  - `400` is final: a block that breaks the format or the limits (`invalid block: <TableBlockError>`), an empty producer id or a negative producer sequence (`invalid producer: …`). The collector drops the block and counts it.
  - **`503` is a fault, never a refusal**: the block store could not take the block (a failed write, too little disk, a store reopening after a failure). Nothing was stored. The collector keeps the block and sends the same frame again (same producer id and sequence) after `retryAfter` seconds. A `500` (`failed to store the block`, an error of the server) is handled the same way.
- **Access** is checked again for every binary frame, as for every request on the session: a key that can no longer send closes the connection with `1008`, and the block is not stored. A block counts as a use of the key; a probe does not.
- **One block in flight (stop and wait).** A collector sends the next block after the ack of the previous one. **The order is a rule the server relies on, not only what the transport happens to do**: the merge (#1850) keeps, per product and producer, the highest producer sequence it has merged and answers anything at or below it as a duplicate. So a producer sends its blocks in sequence order; it never sends a lower sequence after a higher one was acknowledged; a block that got a final refusal (`allRefused`, `400`) is dropped and its number is not reused; its sequence never goes back, across restarts included; and a collector that cannot read its producer state (the id and the next sequence, kept with its file of undelivered blocks) takes a new producer id and starts again.
- **Blocks in flight when a connection drops** may or may not have been stored. The collector sends the same block (same producer id and sequence) on its next table connection; one that was stored is answered `duplicate`. A block that a replaced connection is still storing when the resend arrives is stored once: the store appends in the order it reserved and the second put finds the first.
- **A collector's rules, besides the order above**: a table goes into a block only after its registration was acknowledged without an error under its path on the session (the two connections are separate sockets, and a block could overtake its registration); the table identifier is the registered path in normal form (see "Table sensor registration"); on `UnknownSensor` for a table it registered, the collector registers that table again (an operator may have removed the sensor) and the rows of the refused block are lost and counted; each collector installation has its own producer id (the server logs a `Warn`, at most once a minute, when two live table connections of one product send blocks of one producer id, and refuses nothing).
- **Shutdown**: the table connection is closed with `1001` like the session, after the ack of a block being stored (within the shutdown budget).
- **Back-pressure on memory.** A frame larger than 64 KiB waits, unread, while the server holds as much for table frames as it allows (`CollectorSessions.MaxTableBytesInFlight`); a collector sees only a slower send. A wait longer than twice the silence limit (3 minutes by default) closes the connection with **`1013`** (Try Again Later): the frame was not complete, so there is no request id to answer; the block was not stored, and the collector sends it again on its next table connection. **A reserved frame has a deadline**: from the moment the server reserves its bytes, the whole frame must arrive within the silence limit (90 s by default; 4 MiB in that time is about 47 KB/s), however its bytes trickle in, or the connection is cut and the reservation goes back. **One access key holds at most one reservation at a time**: a second large frame of the same key (another connection of the same key) waits for the first like any other waiter, so the connections of one key cannot fill the budget for the others. **The limit that follows:** collectors that share one access key (a product key installed on many hosts) send their frames larger than 64 KiB one at a time, each holding the key's reservation from its first byte past 64 KiB until its put is answered. With N such collectors sending a large frame at once, the last waits (N − 1) × that time; past twice the silence limit (180 s by default) it is closed with `1013` and sends the block again on its next table connection, queuing again. The bound is passed beyond about 91 collectors of one key at 2 s a frame, beyond 3 at the 90 s worst case of a trickled frame. Frames up to 64 KiB, and collectors with keys of their own, are not affected. A collector that sees repeated `1013`s for this reason needs a key of its own; the server logs such a close as a `Warn` naming its own key as the cause.
- **Close codes from the server on a table connection**: `1000` replaced or its session ended, `1001` shutdown, `1013` the wait for the frame budget passed its bound, `1002` a binary frame without a trusted header or a text request without `id`, `1007` a text frame that is not a JSON object, `1008` the key can no longer send, `1009` a frame over `maxFrameBytes`. The session keeps `1003` for a binary frame: it stays text-only.

### File connection (#1884, ADR-0022 points 6-8)

A collector sends a large file (a dump, up to `fileLimits.maxFileBytes`, 64 GB by design) as a large value of a File sensor on a **third WebSocket of the same session protocol**, opened for an upload and closed after it, so that a piece never waits ahead of a block or a value. Large files need the session transport: there is no HTTP route for uploading them. The store and the value are #1883 (`docs/initiatives/large-file-values.md`).

**Handshake.** `GET {base}session` with `Sec-WebSocket-Protocol: hsm.files.v1` (echoed), the `Key` / `ClientName` headers and `X-Hsm-Session: <sessionId>` of the collector's live ordinary session, as for a table connection. Refusals, in this order: `406` no usable key; `404` sessions off, or **file connections off** (`CollectorSessions.MaxFileFrameBytes = 0`, or a server without a store of large files); `400` no known subprotocol, or both `hsm.tables.v1` and `hsm.files.v1` offered (a connection is one or the other); `503` shutting down; `409` `X-Hsm-Session` missing, malformed, or naming no live session of the same key.

- **The pair.** As the table connection: no slot, one per session (a new one replaces the old; the old is closed with `1000`, reason `replaced by a new file connection of its session`, and gives its upload back **before the new one's hello**: a close the collector does not answer within 3 s is cut off), closed with `1000` `its session ended` when its session ends. A session holds at most three sockets (session, table connection, file connection), so a server holds at most three times `MaxSessions` sockets.
- **Hello.** `{"op":"hello","body":{"maxFrameBytes":4194304,"heartbeatSeconds":30,"fileLimits":{"maxFileBytes":68719476736,"pieceBytes":4173824}}}`. `maxFrameBytes` is `CollectorSessions.MaxFileFrameBytes`. **`pieceBytes`** is the largest multiple of 4 KiB with `88 + ZSTD_compressBound(pieceBytes) ≤ maxFrameBytes`: a piece of that size always fits a frame, however badly it compresses (4,173,824 bytes at the default 4 MiB frame; 16,465 pieces for 64 GiB).
- **Text frames** are those of the session: `ping` / `pong`, acks, `1007` for a frame that is not a JSON object, `1002` for a request without `id`. The requests are `begin`, `end` and `discard`; any other `op` is `400 unknown op`.
- **An answer with `retryAfter` is not final** (whole seconds, at least 1): the collector tries again after it, with the same request unless a row below names another (`503` to `end`, `409` to `discard`: `begin`). Any other 4xx is final for that file until the collector restarts, except where a row below says what to do instead.

**begin** (text): names the file and how it is cut.

```json
{"id":1,"op":"begin","body":{"path":"app/dumps","name":"app_20261125.dmp","size":123456789,"modified":"2026-11-25T10:00:00.1234567Z","source":"vol:5b2e/file:00a1","secure":true,"pieceBytes":4173824}}
{"id":2,"op":"begin","body":{"path":"app/report","name":"report.bin","size":3145728,"modified":"2026-11-25T10:05:00Z","source":"send:5f0c3e2a9b6d4c1e8a7f2b3c4d5e6f70","secure":false,"status":3,"comment":"disk is full","pieceBytes":4173824}}
{"ack":1,"status":200,"body":{"upload":"a3f0…32 hex","next":0,"pieces":30,"pieceBytes":4173824}}
```

`path` is the File sensor's registered path, 1..1,024 characters, looked up only in the root product of the key; `name` 1..1,024 characters, shown and used as the name inside the archive, never a path on the server; `size` the original size in bytes (0 allowed: a file of no pieces); `modified` the source file's modification time with `Z` or an offset (taken as UTC); `source` optional, 1..256 characters, an opaque identity of the source file (a file id, an inode) so that two files with one name, size and time are not taken for one; `pieceBytes` 65,536..the hello's `pieceBytes`; **`secure`** optional, `true` or `false`, **absent means `true`** (ADR-0022 point 20: a secure file is given out as a zip under the product's dump password, a file that is not secure as the same zip without one; an older collector, or a mistake, fails closed), kept with the upload and never changed; `status` optional, the `SensorStatus` number as `Status` of a value in a `list` frame (absent: `1`, Ok); `comment` optional, text of at most 4,096 characters (absent: none); a field out of its bounds is `400`, and `null` is taken as absent for every optional field. Status and comment go into the value. The file is known by its sensor, name, size, `modified`, `source` and `secure` mark, across restarts of either side: a `begin` with the other mark is another upload. A `begin` is a text frame and must fit 64 KiB (a frame above that needs an upload the connection holds); the bounds of its fields keep it well within. The server records with the upload the session's `ClientName` and the address it resolved for it; they are not fields of the request.

**A routed ordinary send** (ADR-0022 point 21): a collector on a session sends the content of an ordinary file value larger than a quarter of the `maxFrameBytes` of the **session's** hello on the file connection, as a large file that is not secure, with the same requests, answers and limits. It has no file of its own, so it names `source` = `send:` and a new GUID (32 hex digits) per send, `modified` = the time of the value, and its value's `status` and `comment`; two sends of one name and size are then never one file. It is sent only after the registration of its sensor was acknowledged on the session: an unknown sensor stays `422`.

**One file connection carries several files in turn**: after a file is answered (`stored`, a final refusal, `discard`), the next `begin` on the same connection starts the next one. A collector keeps the connection while files are waiting, rather than opening one per file; a `begin` on a connection that holds an upload gives that one back first, so a routed send can go ahead of a large file, which then continues from the server's piece number.

| Answer | Meaning |
|---|---|
| `200 {"upload","next","pieces","pieceBytes"}` | A new upload (`next` 0) or a continue: send piece `next` onward. `pieceBytes` is the upload's own, kept from its first begin. `next == pieces`: every piece is in, send `end`. A begin with another `pieceBytes` than the upload was begun with starts it again from 0 (a collector whose hello now allows less does that; never a `400`). |
| `200 {"upload","state":"stored"}` | This file was stored before (an answer that was lost): nothing to send; the collector may delete its source. |
| `202 {"upload","state":"checking"}` + `retryAfter` | Its end check is running: ask again. |
| `503` + `retryAfter` | No room on the server's disk; too many uploads in progress (`LargeFiles.MaxUploadsInProgress`, server-wide); too many unfinished uploads of the product (`LargeFiles.MaxUploadingPartsPerProduct`); a fault of the store. |
| `409` + `retryAfter` | Busy: another live connection holds this file. |
| `413` | Larger than `maxFileBytes`. |
| `422` | The sensor cannot take it: no File sensor at that path in the key's product, blocked, not a File sensor. |
| `400` | A malformed body or a field out of its bounds (checked before the store). |

A connection holds one upload: a `begin` on a connection that holds one gives the old one back first (its pieces stay on the server).

**Piece** (binary): version 1, little-endian, an 88-byte header and one zstd frame of the piece.

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | frame version, `1` |
| 1 | 1 | kind: `1` piece, `2` probe |
| 2 | 2 | reserved, `0` |
| 4 | 4 | header length (u32): where the zstd frame starts; `88` in version 1; the server accepts up to 1,024 and skips the rest |
| 8 | 8 | request id (i64), echoed as `ack` |
| 16 | 16 | upload id: the 16 bytes whose lower-case hex is the `upload` of the begin answer |
| 32 | 8 | piece number (i64): piece `n` holds bytes `n × pieceBytes ..` of the file |
| 40 | 4 | decompressed length (u32): `pieceBytes`, the last piece the remainder |
| 44 | 4 | compressed length (u32): the bytes after the header |
| 48 | 4 | CRC-32 (IEEE, as the table block's) of the compressed bytes |
| 52 | 4 | reserved, `0` |
| 56 | 32 | SHA-256 of the piece's original (decompressed) bytes |
| 88 | … | exactly one zstd frame that states its content size (zstd level 3 in the collectors); nothing for a probe |

A frame shorter than 88 bytes, of another version or kind, with a reserved byte set, or with a header length below 88, above 1,024 or past the frame, cannot be answered (its id is not trusted) and closes with **`1002`**. A frame over `maxFrameBytes` closes with `1009`. **A frame larger than 64 KiB on a connection that holds no upload closes with `1002`**: only a piece is that large, and a piece needs an accepted `begin`. A **probe** is the 88-byte header with kind `2` and zeros after the request id; it is answered `{"ack":<id>,"status":200,"body":{}}`.

One piece in flight: the collector sends the next after the answer. The server checks a piece in this order, **without decompressing it**:

| Check | Answer when it fails |
|---|---|
| the upload id is the upload this connection holds | `404`: begin again |
| `0 ≤ number < pieces` | `400 …: the upload is discarded` |
| the decompressed length is what that number must be; the compressed length is the rest of the frame and at most `ZSTD_compressBound(decompressed length)` | `400 …: the upload is discarded` |
| the CRC-32 | `409 {"next":n,"reason":"checksum mismatch"}`: the piece is refused, the upload kept; send it again |
| the zstd frame, read by zstd's own functions: exactly one frame (not skippable) over every byte, stating its content size, equal to the decompressed length, with a window of at most `pieceBytes` rounded up to a power of two | `400 …: the upload is discarded` |
| the store | below |

| Answer of the store | Meaning |
|---|---|
| `200 {"next":n+1}` | On the server's disk, with its record, before this answer. |
| `200 {"next":N}` | A repeat of a piece the server has (`number < N`): stored once, nothing written; `N` is the server's next. |
| `409 {"next":N,"reason":"out of order"}` | A piece ahead of the server's next: nothing written; go on from `N`. |
| `503` + `retryAfter` | The write failed (a full disk in the middle included): nothing acknowledged; send the same piece again. |
| `404` | The upload is no longer held by this connection (removed, its lease lost): begin again. |

A `400` that discards the upload removes its part from the server; the connection holds no upload after it, and the collector begins again from piece 0.

**end** (text): the hash of the whole file.

```json
{"id":31,"op":"end","body":{"upload":"a3f0…32 hex","hash":"<64 hex>"}}
{"ack":31,"status":202,"body":{"upload":"a3f0…","state":"checking"},"retryAfter":1}
{"ack":32,"status":200,"body":{"upload":"a3f0…","state":"stored","time":"2026-11-25T12:00:00.0000000Z"}}
```

**`hash` is the SHA-256 of the pieces' SHA-256 digests**, in piece order (the 32-byte digests concatenated); of a file of no pieces, the SHA-256 of nothing. Each side can continue after a cut with a list of 32 bytes per piece instead of a running hash. The value the server records shows the SHA-256 of the original bytes, which the check computes on the way (#1883).

The check reads every stored piece, checks its zstd frame again, decompresses it into a buffer of exactly its length with the window bounded as above, and compares its SHA-256 with its digest; then the hash. For a large file it takes minutes, so **`end` waits for the check at most 2 seconds**: a check done by then is answered with its outcome (a small file, a routed send); a longer one with **`202 checking` and `retryAfter`** (about the time the check reads the file at 256 MiB a second: 1 to 30 seconds), and the collector sends the same `end` again until the answer is not `202`; pings keep the connection alive meanwhile. The check runs beside the connection and goes on if the connection closes; after a cut, or a restart of either side, the collector sends `begin` again, which answers `checking`, `stored`, or `next == pieces` (the check was cut by a restart: send `end`, it runs again).

| Answer to `end` | Meaning |
|---|---|
| `202 {"state":"checking"}` + `retryAfter` | The check runs: ask again. |
| `200 {"state":"stored","time"}` | Verified on the disk and the value exists (`time` is the value's time, the time the server accepted the file). Only now does its alert fire; the collector may delete its source. |
| `409 {"next":N,"reason":"pieces are missing"}` | Not every piece is in. **After any `409` to `end` the collector sends `begin` again**, which answers the server's `next`: once the check has begun (a `202` came before), the connection no longer holds the upload, and a piece sent instead would be closed with `1002` as a large frame without an upload. |
| `400 "<what>: the upload is discarded"` | The check failed: the hash, or piece `k` (`piece k: its bytes do not match its digest`, `piece k: the piece does not decompress …`). The part is removed; begin again from 0. |
| `422` | The sensor was removed or can no longer take the file. |
| `404` | No such upload on this connection: begin again. |
| `503` + `retryAfter` | The check could not read the part: nothing changed; begin again after `retryAfter` (`next == pieces`), then `end`. |

After an `end` was answered, the connection holds no upload; a repeated `end` of it gets the same answer.

**discard** (text): the collector gives up an upload because its source file changed while it was sent (ADR-0022 point 3: a changed file is not finished; it is sent again once it has settled, as a new file).

```json
{"id":12,"op":"discard","body":{"upload":"a3f0…32 hex"}}
{"ack":12,"status":200,"body":{"upload":"a3f0…","state":"discarded"}}
```

It names the upload this connection holds. After a cut, the collector first sends `begin` with the file's **original** identity (name, size, `modified` and `source` as first begun) to hold it again; if that `begin` answers `stored`, the file was accepted before it changed and there is nothing to discard. Then it begins the changed file with its new identity: a new upload from piece 0.

| Answer to `discard` | Meaning |
|---|---|
| `200 {"upload","state":"discarded"}` | The part, its reserved space and its upload slot are gone; the connection holds no upload. |
| `409 {"upload","state":"checking"}` + `retryAfter` | `end` was sent and its check is running: the check settles the upload. Send `begin` after `retryAfter`: `stored`, or `next` after a failed check (then discard it). Sending `discard` again instead is harmless: `409` while the check runs, `404` after it, then `begin`. |
| `404` | This connection holds no such upload: `begin` it first. |
| `503` + `retryAfter` | The store could not remove it: nothing changed, the connection still holds it; send the same `discard` again. |
| `400` | A malformed body. |

- **Access** is checked again for every frame, as on the table connection: a key that can no longer send closes with `1008`, and the pieces stored so far stay for a continue. A piece counts as a use of the key, a probe does not. A check that has begun goes on after the key lost access: the file was sent whole while the key could send.
- **Shutdown**: `1001`, after the answer of a piece being written; a piece still arriving is not stored. Running checks are cancelled and awaited; a cancelled check changes nothing.
- **Memory.** A connection holds at most one upload, and frames above 64 KiB only while it holds one, so the memory of file frames is bounded by the uploads in progress (`LargeFiles.MaxUploadsInProgress`), not by a budget: there is no wait for memory and no `1013` on a file connection. An upload beyond the bound is refused at `begin` (`503` with `retryAfter`). **There is no per-key rule** (ADR-0022 point 6): collectors that share a key do not queue behind each other's files, and one key can hold every upload slot. A frame above 64 KiB must arrive whole within one silence limit from its 64th KiB (90 s by default: 4 MiB needs about 47 KB/s), or the connection is cut.
- **Close codes from the server on a file connection**: `1000` replaced or its session ended, `1001` shutdown, `1002` a header that cannot be trusted, a text request without `id`, a frame over 64 KiB without an upload, `1007` a text frame that is not a JSON object, `1008` the key can no longer send, `1009` a frame over `maxFrameBytes`.

## Table history (#1845, ADR-0021 point 13)

`POST {base}tableHistory` reads the rows of one table sensor in `[From, To)`, for the client library (blocks) and for clients without it (CSV, JSON). The same `Key` header as every route, and only the header: the obsolete body `Key` of `BaseRequest` is not honoured on this route (it is on `history` and `historyFile`). The key must be able to read (`CanReadSensorData`), and the sensor is found by its path in the root product of the key, in normal form (`/a// b` finds `a/b`). Rows not merged yet and, after #1850, merged ones, joined by time; equal times in the order they arrived.

```json
{"Path":"quotes/EURUSD","From":"2026-11-25T09:00:00Z","To":"2026-11-25T10:00:00Z","MaxRows":100000,"Format":0}
```

- **Times** are UTC (a time without a kind is UTC). `MaxRows` is optional and capped: by `TableSensors.MaxTextReadRows` (100,000 by default) for CSV and JSON, by `TableSensors.MaxReadRows` (1,000,000) for blocks.
- **A cut answer ends at a time boundary.** When more rows match than the limit allows, the answer holds the earliest ones but never only some of the rows of one time: those are left for the next page, which starts at their time. `X-Hsm-Next-From` (and `nextFrom` in JSON) is where the next page starts: send it as `From` and every row comes exactly once. The one exception is a single time with more rows than the limit: the answer is those rows cut at the limit, `X-Hsm-Rows-At-Time-Omitted: true` says the rest of that time is skipped, and the next page starts one tick later; when that time is the last tick of the range there is no next page. A client that needs those rows asks with a larger `MaxRows`. **A client pages until an answer has no `X-Hsm-Next-From`** (no `nextFrom` in JSON); `X-Hsm-Truncated` says whether rows were left out, not whether there is a next page.
- **Headers** of every answer: `X-Hsm-Row-Count`, `X-Hsm-Truncated` (`true` / `false`), `X-Hsm-Next-From` (ISO, when there is a next page), `X-Hsm-Rows-At-Time-Omitted` (only when `true`), `X-Hsm-Blocks-Failed` (stored blocks that could not be read: of hours not merged yet, and of merged hours whose merge kept them as unreadable, #1850 part 4b; such a block counts whether or not it held rows of the table asked for, which cannot be known), `X-Hsm-Batches-Failed` (batches of merged hours that could not be read, #1850). Both are always present, in every format; JSON also carries them in the body as `blocksFailed` and `batchesFailed`. **The answer is incomplete when either is not `0`**: the rows of those blocks or batches are missing, and asking again returns the same answer until the store is repaired, so a client reports it (an error, or a warning next to the rows) and does not treat the rows as the whole range. Two figures, not one sum: they name the store that is damaged.
- **JSON** (`Format` 0, `application/json`): the columns, the counts and the rows as arrays in column order. A time is ISO 8601 with seven fraction digits (exact to the tick); a double is the shortest text that reads back to the same value; NaN and the infinities, which JSON numbers cannot carry, are the strings `"NaN"`, `"Infinity"`, `"-Infinity"`; a cell without a value is `null`. An int64 beyond 2^53 is written as the exact number, which JavaScript readers round.

```json
{"path":"quotes/EURUSD","columns":[{"name":"time","type":"time"},{"name":"bid","type":"double"},{"name":"size","type":"int64"}],"rowCount":3,"truncated":true,"nextFrom":"2026-11-25T09:00:00.3000000Z","rowsAtTimeOmitted":false,"blocksFailed":0,"batchesFailed":0,"rows":[["2026-11-25T09:00:00.1000000Z",1.0841,100],["2026-11-25T09:00:00.2000000Z",null,7],["2026-11-25T09:00:00.2500000Z","NaN",5]]}
```

- **CSV** (`Format` 1, `text/csv; charset=utf-8`): RFC 4180, CRLF line ends, a header line with the column names (quoted when a name holds a comma, a quote or a line break), the values as in JSON without quotes; **a cell without a value is empty**.

```
time,bid,size
2026-11-25T09:00:00.1000000Z,1.0841,100
2026-11-25T09:00:00.2000000Z,,7
2026-11-25T09:00:00.2500000Z,NaN,5
```

- **Blocks** (`Format` 2, `application/vnd.hsm.table-blocks`): the rows as table blocks of the one table (identifier: the sensor path; schema id: the sensor's; null maps as the format defines), one after another, at most 65,536 rows each; a reader takes them apart by the compressed length in each 44-byte header. `X-Hsm-Table-Columns: time;1,bid;3,size;2` names the columns (names percent-encoded, types the format's numbers). No rows, no body. `MaxBlockFormatVersion` (default 1) is the newest block format the client reads: the server never writes a newer one, and answers 400 when it cannot write one that old.
- **Statuses**: `200`; `400` a body that cannot be read, no path, `From` not before `To`, an unknown format, `MaxRows` not positive, a block format the server cannot write, a sensor that is not a table sensor; `404` no sensor with that path in the product of the key; `406` no key, or a key that may not read; `503` with `Retry-After: 60` when the block store cannot be read now, or when the server failed while it looked the access key up (a fault, the same request later is answered; not the `406` of a key that may not read); `503` with `Retry-After: 1` and the text `The table store changed during the read; the request can be sent again.` when the store changed (an hour was merged, a week dropped) while each of three plans of the read was taken (#1847): nothing was read, and the same request a second later is answered.
- **Cost**: reads run at most `TableSensors.MaxConcurrentReads` (2) at a time, a count shared with the table pages of the web UI (`TableReadGate`, #1847); others wait. Until a range is merged (#1850) a read decompresses every stored block whose time range meets it, whatever `MaxRows` is: about 60–160 ms per hour of one table. A client that goes away stops the read before its next block.

## Invariants

- Never renumber or reuse enum values; never rename JSON fields; additive evolution only.
- Server tolerates unknown/omitted optional fields; collectors must tolerate unknown response fields.
- `ProductEntity.Policies` (non-TTL persisted policy-id list on a product) is deprecated and no longer populated by the server. Node-level alerts on Folders/Products were removed in #1142; the collector wire format is unaffected because template materialization targets sensors only.
- **Batch storage semantics (#1441):** values are stored regardless of their order inside a batch. An out-of-order value (older than the sensor's cached newest) is persisted directly, subject to a retention floor: a value older than the sensor's KeepHistory window (when configured — the retention pass purges such rows anyway) or than the last history clear is NOT stored (counted as `OutOfRetentionValues`). The clear half of the floor is stamped by BOTH clear paths — explicit operator clears and the automatic KeepHistory retention pass (monotone max), so widening KeepHistory does not lower it until restart: backfill into the newly opened older window is conservatively refused (counted, rate-limited warn) for the current uptime. The floor is bounded ONLY by KeepHistory and clears: with Forever retention (KeepHistory = None/never configured, never cleared) the floor is unset, so such sensors accept arbitrary past timestamps and the weekly-database protection does not apply to them. The floor deliberately does not use the oldest stored row as a boundary: backfill older than the first stored row but inside the retention window IS stored — whether data survives must not depend on the server's restart history. An out-of-order instant value is otherwise identical to before the change — validated, its alerts/notifications evaluated, `ReceivedNewValue` delivered to charts and live views — it just never becomes the cached newest value. The floor check runs at the persistence step, AFTER validation and live delivery: a value refused by the floor may therefore have already fired alerts and been delivered to live views while never appearing in history (the same was true of any out-of-order value before #1441 — the refusal only skips the database write). With `AggregateValues` on, an older value with EQUAL content folds into the cached newest (no row of its own, pre-#1441 semantics) when both fall in the same storage week (Monday 00:00 UTC); from an earlier week it is an ordinary out-of-order value of that week (a fold, newer or older, never crosses the week boundary); a non-foldable older value whose tick lands ON or INSIDE an existing aggregated span row is NOT stored (counted as `AggregateSpanOverlapsSkipped`) — a direct write there would replace the whole stored run (the DB key is the span's first tick) or add a row overlapping the span; other non-foldable older values take the direct write. **Bar sensors never take the direct write**: same-period partials merge in memory by `OpenTime`, and a bar row's DB key is its SEND time — persisting a late partial directly would duplicate the period when the completed bar lands. Within one timestamp the LAST WRITTEN value keeps the row (the storage key is `(sensorId, ticks)`, a compatibility-frozen format): a value landing on the cached newest's tick supersedes the row just written for it, and a non-aggregate sensor's out-of-order write at a tick that already has a row overwrites that older row (not counted — detecting it would need a read-before-write on the hot path; aggregate sensors DO pay that read on their cold out-of-order path, see `AggregateSpanOverlapsSkipped`). Values rejected by the singleton gate (or a type mismatch) are counted but not stored; the batch response stays empty for them (a rejected value is a sensor-level event, not a transport error). A 200 response therefore means "transport accepted", not "every value stored". The visibility surface is the `hsm-server` log plus internal per-sensor diagnostics — `OutOfOrderValuesStored`, `SameTickValuesSuperseded`, `RejectedValues`, `OutOfRetentionValues`, `AggregateSpanOverlapsSkipped` — which are in-memory only (reset on restart, not yet surfaced through API/UI); the out-of-order, supersede, out-of-retention and aggregate-span Warns are rate-limited to the first occurrence and every 1000th (carrying a running total), and rejections are counted without per-value logging (singleton rejections are steady state).

## Native port (C++)

The native collector (`src/native/collector`, #1096) reproduces this wire **byte-for-byte** against the **net8 / Core** `System.Text.Json` output (the shortest-double runtime; net472 doubles diverge and are out of scope). Doubles follow the .NET Core layout — fixed notation for decimal exponents -4..16, scientific from 1e17 and below 1e-4 — since native 0.11.1; before that native switched to scientific from 1e15 (.NET Framework's `"R"` layout), so a value such as 1e15 went out as `1E+15` where STJ writes `1000000000000000` (#1528, pinned by `number_format_contract:double_wire_text_matrix`). `BuildWire{Value,Bar,File,Registration}Json` in `hsm_collector.cpp` emit the exact property order (most-derived-first, base-last, `Type` first), `Key:null`, ISO-8601-Z time (fraction trimmed), TimeSpan ".NET c", `List<byte>` numeric array, the native-only bar `StdDev` right after `Type` (`null` when unknown; #1509/#1529), `Percentiles:null`, and the full `AddOrUpdateSensorRequest` shape. String escaping goes through a dedicated `EscapeJsonWire` that mirrors the default `JavaScriptEncoder` (the internal-conformance `EscapeJson` keeps its own simpler `\"` convention and must not be confused with it). Parity is locked from both sides: native `native_wire_*` unit tests pin the exact bytes (including double/bool/double-bar, the `<>&'+"`/non-ASCII escaping cases, the int-bar half-to-even mean, and pre-epoch / `Int64.MinValue` time edges), and `WireFormatGoldenLockTests` (net8 IntegrationTests) asserts the **same** strings against the real `HttpRequest<T>` serializer — if .NET drifts, that test fails first and both sides update in lockstep.

## Key Files

| File | Purpose |
|---|---|
| `src/api/HSMSensorDataObjects/**` | DTOs + enums (source of truth) |
| `src/collector/HSMDataCollector/Client/HttpsClient/Endpoints.cs` | Route constants |
| `src/server/HSMServer/SensorApi/Sessions/SessionProtocol.cs` | Collector session: names, close codes, frame writers (server) |
| `src/server/HSMServer/SensorApi/Sessions/Files/FilePiece.cs` | File connection (#1884): the piece header, the piece size of a frame, the zstd checks of a piece |
| `src/server/HSMServer/SensorApi/AgentDirectives.cs` | The server directives of an agent, one set for the `X-Hsm-Directive` headers and the session's `directive` pushes |
| `src/collector/HSMDataCollector/Converters/*.cs` | Serializer configuration |
| `ai-docs/Wiki/REST-API.md` | Human-facing examples |

## Dependencies

- Used by: collector `data-pipeline`/`http-client`, server controllers, `src/wrapper`, native C++ port.
