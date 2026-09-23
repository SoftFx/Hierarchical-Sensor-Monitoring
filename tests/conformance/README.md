# Collector conformance corpus (`.hsmtest` DSL)

The single parity oracle for every DataCollector implementation (epic #1093,
workstream #1094). Test cases are **data files**, owned once in this
directory; each language ships a thin **driver** that maps DSL verbs to its
collector API and evaluates expectations against captured payloads. Drivers
contain **zero test logic** — all semantics live in the scenario files. A
third language joins by writing one driver; the corpus is untouched.

Current drivers:

| Driver | Runner | Fixture discovery |
|---|---|---|
| .NET (canonical) | `src/collector/HSMDataCollector.Tests/CollectorConformanceTests.cs` | auto: globs `collector/*.hsmtest` (non-recursive) |
| native C++ | `src/native/collector/tests/hsm_collector_tests.cpp` | explicit: `add_conformance_test` in the native `CMakeLists.txt` |

CI: `collector-unit-tests` runs the corpus through the .NET driver;
`native-collector-conformance.yml` builds the native driver and runs it on
Windows + Linux. **A new fixture must land in the same PR as its .NET verb
support and its CMake registration** — the .NET driver discovers it
automatically and would otherwise fail on unknown verbs.

## File format

```
# comment (whole-line only)
case_name|action|arg1|arg2|...
```

- One step per line, fields separated by `|`. There is no escaping — an arg
  cannot contain `|` or a newline (use text tokens below for hard-to-type
  content).
- Lines starting with `#` and blank lines are skipped.
- All steps sharing a `case_name` form one test case, executed in file order.
  Cases are independent: each gets a fresh collector/state; case order within
  a file is not guaranteed across drivers.
- Files are UTF-8 with **LF endings, enforced by `.gitattributes`** (the
  corpus must be byte-identical on every platform).
- Avoid *trailing* empty fields (`...|`) — `std::getline`-based splitting
  drops them; make trailing optional args explicit or omit them.
- Header convention: start each fixture with a comment block naming the
  contract area and documenting any verbs it introduces (`# verb|arg|...`).

### Text tokens

Args are literal text except these tokens, expanded by every driver:

| Token | Expands to |
|---|---|
| `token:null` | language null (C# `null` / C++ `nullptr`) |
| `token:blank` | `" \t "` (whitespace-only) |
| `token:json-special` | `quote"slash\tab<TAB>newline<LF>` |
| `token:control-01` / `-02` / `-1f` | strings embedding U+0001 / U+0002 / U+001F |
| `repeat:<ch>:<count>` | `<ch>` repeated `<count>` times |
| `NaN`, `Infinity`, `-Infinity` | IEEE-754 specials (double args only) |
| `Ok`, `Warning`, `Error` | sensor status names (raw numeric in `*_raw` verbs) |

## Canonical payload text

Expectations match against a **canonical serialized text** of each captured
payload — fixed field order, invariant culture, doubles in .NET `"R"`
(shortest round-trip) format. Both drivers emit the identical shape; it is
part of the contract:

```
instant:  {"Type":N,"Path":"...","Value":V,"Status":N,"Comment":"..."}
bar:      {"Type":N,"Path":"...","Min":m,"Max":M,"Mean":u,"First":f,"Last":l,
           "Count":c,"OpenTimeMs":o,"CloseTimeMs":e,"Status":N,"Comment":"..."}
file:     {"Type":6,"Path":"...","Value":"utf8 content","Name":"...",
           "Extension":"...","Status":N,"Comment":"..."}
```

`Type` is the numeric wire discriminator (bool 0, int 1, double 2, string 3,
int bar 4, double bar 5, file 6, rate 9, enum 10). `OpenTimeMs`/`CloseTimeMs`
are unix milliseconds. String fields are JSON-escaped (`"` `\` control chars).

## Verb catalog

Status args accept `Ok|Warning|Error`; `sensor_index`/`bar_index`/`idx` index
into the per-case creation order of that sensor kind (0-based).

### Collector lifecycle

| Verb | Semantics |
|---|---|
| `create_collector` | collector + recording sender, default queue limits |
| `create_collector_with_identity\|computer_name\|module` | identity affects payload path prefix |
| `create_collector_with_limits\|max_queue_size\|max_values_in_package\|collect_period_ms` | queue/batching limits |
| `expect_create_collector_rejected\|access_key\|server_address[\|port]` | options validation throws |
| `start` / `stop` | lifecycle transitions (graceful stop flushes the data queue) |
| `repeat_start_stop_add\|cycles\|sensor_index\|status\|comment_prefix` | restart cycling with one add per cycle |
| `stop_expect_under_ms\|bound_ms` | stop must return within the bound (shutdown boundedness) |
| `sleep_ms\|milliseconds` | wall-clock wait — rollover and sampled-bar partial-post cases only, keep rare |

### Sensor creation

| Verb | Semantics |
|---|---|
| `create_int_sensor\|path` (also `bool`, `double`, `string`, `enum`) | instant sensor |
| `create_int_sensors\|count\|path_prefix`, `create_mixed_instant_sensors\|count\|path_prefix` | bulk creation for cardinality cases |
| `create_last_int_sensor\|path\|default_value` (also `bool`, `double`, `string`) | last-value sensor; default posts on stop if never updated |
| `create_int_bar_sensor\|path\|bar_period_ms\|post_period_ms` | `post_period_ms=0` ⇒ inert periodic posting; `BarTickPeriod` always inert |
| `create_double_bar_sensor\|path\|bar_period_ms\|post_period_ms\|precision` | double bar with rounding precision |
| `create_int_bar_sensor_with_partial_posts\|path\|bar_period_ms\|bar_tick_ms\|post_period_ms` | (#1428) a push-fed IntBar (values via `add_bar_int`) on the built-in bar schedule: a partial of the in-progress bar every `post_period_ms` (aligned) with a stable OpenTime, and a roll on the `bar_tick_ms` tick that publishes the closed bar without a further value. C#: `CreateIntBarSensor` with a live tick/post (`PublicBarMonitoringSensor`, base of the queue-diagnostic bars); native: the same test hook without a sample source (the queue-diagnostic / network-speed bar path) |
| `create_sampled_double_bar_sensor\|path\|bar_period_ms\|bar_tick_ms\|post_period_ms\|precision` | (#1428) the machinery behind the metric-driven default bars (Total CPU, Free RAM, Process CPU/memory/threads): a driver-owned source is sampled every `bar_tick_ms` (the n-th sample is n) into a bar aligned to `bar_period_ms`, and a partial of the in-progress bar is posted every `post_period_ms` (aligned to multiples of it) with a stable OpenTime; stop flushes the partial. C# registers a `CollectableBarMonitoringSensorBase` subclass; native uses the test-only hook `hsm_collector_test_create_sampled_bar_sensor` + a counting metric-source factory. Wall-clock: pair with the timing-immune bar invariants below |
| `create_failing_metric_sensor\|path\|post_period_ms\|message` | (#1426) a Double value sensor whose live read FAILS every tick with `message`, leaving the source usable. Native binds a metric source answering `HSM_METRIC_READ_SAMPLE_ERROR`; C# registers a `MonitoringSensorBase<double>` whose `GetValue` throws the same message. Both must then route the failure to the error channel (deduplicated log + `.module/Collector errors`, as `Sensor: <path>, <reason>`) and post one value per period with the default value, status Error and the message as the comment |
| `create_disk_prediction_sensor\|path\|calibration_requests\|post_period_ms\|refresh_period_ms\|free_space_start\|drain_per_second` | (#1426, reworked in #1445) the "Free space on disk prediction" sensor over a SCRIPTED free-space series (free space changes linearly with elapsed time — a function of the clock, not of the call count, so both drivers see the same series), with fixture-sized periods; the catalog row runs 5 min posts / 30 s sampling / 6 calibration requests. `drain_per_second` is SIGNED: positive drains the disk, zero holds it flat, negative frees space, which is how the idle and growing states are reached. The source samples free space every `refresh_period_ms` to feed the SIGNED drain-speed EMA (every interval folded, `prev*0.9 + cur*0.1`, calibration counted on that sampling clock) and posts a TimeSpan every `post_period_ms`. Exactly one of five states is posted and only the first carries a real estimate: draining below the ceiling → `free_space / speed` + Ok + `"Free space decreases by X Mbytes/sec."`; draining beyond the ceiling → `365.00:00:00` + OffTime + `"... More than 365 days left."`; not decreasing → `365.00:00:00` + OffTime + `"Free space is not decreasing. Value cannot be calculated."`; increasing → `365.00:00:00` + OffTime + `"Free space increases by X Mbytes/sec. Value cannot be calculated."`; calibrating → `365.00:00:00` + OffTime + `"Calibration request (n/N). Value cannot be calculated yet."`. `00:00:00` means a disk with no free space left and is never a placeholder. C# registers `FreeDiskSpacePredictionBase` over a scripted `IDiskInfo`; native binds a metric source running the same `disk_prediction.hpp` math. Wall-clock: the NUMBER of calibration posts is not contractual (managed schedules its first post in `InitAsync` while the sampler's first tick is aligned to the next period boundary), so assert the opening post's shape and the LAST payload, not a fixed count |
| `create_rate_sensor\|path\|post_period_ms` | rate = sum / measured elapsed; first post immediately on start |
| `create_function_int_sensor\|path\|post_period_ms\|constant` | driver callback returns `constant` |
| `create_values_function_int_sum_sensor\|path\|post_period_ms\|max_cache_size` | driver callback sums the sliding-window snapshot |
| `create_file_sensor\|path\|default_file_name\|extension` | string-content file sensor (disk `SendFile` is not portable) |
| `create_int_sensor_with_options\|path\|ttl_ms\|unit\|description` | `ttl_ms=0` ⇒ no TTL; `unit=-1` ⇒ unset (codes per the managed `Unit` enum) |
| `create_enum_sensor_with_options\|path\|description\|key:value:color:desc[;...]` | enum sensor with `EnumOptions` (values must not contain `:` or `;`) |
| `create_timespan_sensor\|path`, `create_version_sensor\|path` | instant TimeSpan (type 7) / Version (type 8) sensor |
| `create_int_sensor_full_options\|path\|ttl_ms\|unit\|keep_history_ms\|self_destroy_ms\|statistics\|is_singleton\|aggregate\|grafana\|is_computer\|sensor_location\|description` | full SensorOptions surface + path model; tri-state (is_singleton/aggregate/grafana) -1=null/0/1; statistics -1=null else flags (EMA=1); is_computer anchors at the computer node + forces singleton; sensor_location 0=Module/1=Product |
| `create_service_commands_sensor` | service-commands sensor (string, fixed `.module/Service commands`, implicit received-new-value alert) |
| `add_default_sensor\|id` | register a built-in catalog sensor by stable id name (`total_cpu`, `process_memory`, `free_disk_space`, `service_status`, `collector_alive`, `queue_overflow`, …). Native calls `hsm_collector_add_default_sensor`; C# records the real managed prototype's `AddOrUpdateSensorRequest`. Registration `Path` is asserted by SUFFIX (`.computer/…`/`.module/…`) — the native driver applies the collector prefix while C# records the prototype path. `Description` is not pinned (machine-specific in .NET); byte-exact alert parity is locked by `WireFormatGoldenLockTests` / `NativeDefaultSensorWireMatchesNet` |
| `add_collector_monitoring_sensors` | register the `.module` collector-monitoring group — Service alive + Collector version + Collector errors. C# calls `AddCollectorMonitoringSensors` on the host's collection (Unix/Windows route to one implementation, but each refuses the other platform); native calls `hsm_collector_add_collector_monitoring_sensors`. The heartbeat then beats on each implementation's own post period (managed: the sensor's `PostDataPeriod`; native: the collector's package-collect period), so keep the case well inside the managed 15 s `PostDataPeriod`, pair it with a long `create_collector_with_limits` period for the native side, and rely only on the beat that fires immediately on Start |
| `create_int_sensor_with_alerts\|path\|ttl_ms\|unit\|description` | int sensor consuming the staged alert builders (see Alert builder below) |
| `dispose_sensor\|sensor_index` | release without flushing |
| `expect_create_int_sensor_rejected\|path`, `expect_create_last_*_sensor_rejected\|path\|default_value` | creation validation throws |
| `expect_conflicting_mixed_creates_rejected_parallel\|worker_count\|path_count\|path_prefix` | type conflicts on one path rejected under parallel registration |

### Data actions

| Verb | Semantics |
|---|---|
| `add_int\|sensor_index\|value\|status\|comment` (also `bool`, `double`, `string`, `enum`) | instant add |
| `add_int_sequence\|sensor_count\|values_per_sensor\|start_value\|status\|comment`, `add_mixed_instant_sequence\|set_count\|values_per_set\|start_value\|status\|comment` | bulk sequential adds |
| `add_int_parallel\|worker_count\|values_per_worker\|sensor_count\|status\|comment`, `add_mixed_instant_parallel\|worker_count\|values_per_worker\|set_count\|status\|comment` | concurrent adds (deterministic value sets) |
| `add_bar_int\|bar_index\|value`, `add_bar_double\|bar_index\|value` | bar accumulation; double NaN/±Infinity silently skipped |
| `add_bar_int_sequence\|bar_index\|count\|start_value\|step` | bulk bar adds |
| `add_bar_int_parallel\|bar_index\|worker_count\|values_per_worker\|start_value` | worker *w* adds `start + w*per_worker + i` |
| `add_int_bar_partial\|bar_index\|min\|max\|mean\|first\|last\|count`, `add_double_bar_partial\|...` | partial-bar merge; inconsistent partials silently skipped (int strict, double tolerance `max(1e-12,\|max-min\|*1e-9)`) |
| `add_rate\|idx\|value\|status\|comment` | rate increment; invalid value/status silently dropped |
| `add_rate_raw\|idx\|value\|raw_status\|comment` | raw numeric status (invalid-status cases) |
| `add_function_value\|idx\|value` | buffers into the values-function sliding window |
| `add_file_value\|idx\|content\|status\|comment` | UTF-8 content; `token:null` silently ignored |
| `add_timespan\|idx\|ticks\|status\|comment` | TimeSpan value (`ticks` = 100-ns units); serialized "c" format |
| `add_version\|idx\|major.minor[.build[.revision]]\|status\|comment` | Version value; trailing absent components dropped |
| `service_send_restart\|start\|stop\|update\|idx\|initiator` | service command; value = "Service restart/start/stop/update", comment = "Initiator: <x>" |
| `service_send_update_version\|idx\|initiator\|new[\|old]` | "Service update to <new>" / "Service update from <old> to <new>" |
| `service_send_custom\|idx\|command\|initiator` | arbitrary command string with the initiator comment |
| `expect_add_int_rejected\|sensor_index\|value\|raw_status\|comment` (also `bool`, `double`, `string`, `enum`) | add must throw (validation); previous state preserved |

### Alert builder

Build an alert incrementally, then `alert_stage` it; a following `create_*_with_alerts` verb consumes the staged alerts as the sensor's options. Enum args are the **numeric** managed enum values (AlertCombination/AlertProperty/AlertOperation/TargetType/AlertDestinationMode/AlertIcon).

| Verb | Semantics |
|---|---|
| `alert_new\|kind` | start a builder; `kind ∈ instant\|bar\|ttl` |
| `alert_condition\|combination\|property\|operation\|target_type[\|value]` | append a condition; `value` omitted/ignored when `target_type=1` (LastValue) |
| `alert_notification\|template\|destination_mode` | ThenSendNotification |
| `alert_icon\|icon` | AlertIcon → UTF-8 emoji (escaped to `\uXXXX` on the wire) |
| `alert_sensor_error` | raise alert Status to Error |
| `alert_confirmation\|period_ms` | AndConfirmationPeriod (encoded as ticks) |
| `alert_inactivity\|period_ms` | TTL-alert inactivity window (feeds TTLs/TtlAlerts) |
| `alert_disabled` | BuildAndDisable (`IsDisabled=true`) |
| `alert_stage` | finalize the current builder into the pending list |

### Fault injection

| Verb | Semantics |
|---|---|
| `set_sender_fail_next\|count` | next `count` data sends fail before recording; queue must re-enqueue |
| `set_sender_hang` | every data send blocks until the stop path cancels it (dead transport) |

### Tooling

| Verb | Semantics |
|---|---|
| `dump_payloads_to\|file` | writes every captured payload's canonical text, one per line (LF, UTF-8, no BOM) — the differential fuzzer's byte-comparison channel (`scripts/fuzz-conformance.ps1`); the native driver strips its spike-internal `UnixTimeMs` field. The fuzzer compares dumps after a **stable sort by `Path`** — order within one sensor is contractual (FIFO), order across sensors at a stop-flush is not (hash-map iteration). |

### Assertions

`expect_*` verbs throw/assert on mismatch — a failing step fails the case.
Polling assertions re-check until the deadline, then fail.

| Verb | Semantics |
|---|---|
| `expect_sent_count\|count[\|timeout_s]` | polls (default 2 s) until the count **equals**, then asserts equality |
| `expect_sent_count_between\|min\|max\|timeout_s` | polls to ≥min, asserts ≤max |
| `expect_payload_contains\|payload_index\|substring` | substring of canonical payload text; a NEGATIVE index counts back from the end (−1 = last), which is how a wall-clock fixture with a timing-dependent tail addresses its settled payload |
| `expect_payload_not_contains\|payload_index\|substring` | negative match, same index rules |
| `expect_all_payloads_contain\|substring` | every captured payload |
| `expect_payload_value_sequence\|start_payload_index\|count\|start_value` | consecutive int values in delivery order |
| `expect_each_value_once\|start\|count` | every value in `[start, start+count)` delivered exactly once, any order |
| `expect_payload_type_counts\|bool\|int\|double\|string\|enum` | per-type payload counts |
| `expect_comment_length\|payload_index\|length` | pins the 1024-char comment trim |
| `expect_time_marker_comments\|prefix\|count` | exactly `count` payloads carry a lifecycle-marker comment `"<prefix>: dd/MM/yyyy HH:mm:ss"` (the managed `SensorBase.DefaultTimeFormat`). A comment opening with `"<prefix>: "` that loses the shape FAILS, so a broken format cannot hide by dropping out of the count; the instant itself is wall-clock and is never compared |
| `expect_bar_field\|payload_index\|field\|expected` | `field ∈ type\|min\|max\|mean\|first\|last\|count\|status`; numeric compare rel. tolerance 1e-9 (type/count/status exact) |
| `expect_bar_count_total\|expected` | Σ Count over all bar payloads — "no value lost", timing-immune |
| `expect_bar_open_close_aligned\|payload_index\|period_ms` | close−open == period and open % period == 0 (unix ms) |
| `expect_all_bars_aligned\|period_ms` / `expect_bar_open_times_increasing` | invariants over all bar payloads |
| `expect_bar_open_times_nondecreasing` | like the above but partial posts may repeat an OpenTime; it must never go backwards |
| `expect_partial_bars_accumulate` | payloads sharing an OpenTime are snapshots of one bar: same CloseTime and First, Count never shrinking in delivery order |
| `expect_bar_posts_sharing_open_time_at_least\|min` / `expect_distinct_bar_open_times_at_least\|min` | the largest same-OpenTime group / the number of distinct OpenTimes reaches `min` |
| `expect_eventually_payload_contains\|substring\|timeout_s` | polls any payload for the substring |
| `expect_registration_count\|count[\|timeout_s]` | polls the recorded AddOrUpdate registrations (every sensor registers on every start; immediately when created while running) |
| `expect_registration_contains\|index\|substring` | substring of the canonical registration text: `{"Command":"AddOrUpdate","Path":"...","SensorType":N,"TTLTicks":[...]\|null,"OriginalUnit":N\|null,"Description":"..."\|null,"EnumOptions":[...]\|null,"Alerts":[...]\|null,"TtlAlerts":[...]\|null}` — full path incl. identity prefix; TTL in .NET ticks; `Alerts`/`TtlAlerts` are real `AlertUpdateRequest` JSON (numeric enums, emoji escaped) |
| `expect_eventually_value_above\|threshold\|timeout_s` | polls numeric payload values; fails (not passes) on timeout |
| `expect_no_new_payloads_for_ms\|ms` | baseline now; no new payloads during the window |

## Authoring rules

**Verb design.** Verbs are semantic and observable-behavior-shaped
(`add_int`), never implementation-shaped (`enqueue_to_channel`) — they must
survive refactors of either implementation. Every verb costs × number of
languages: prefer reusing existing verbs; adding one requires implementing it
in **all** drivers in the same PR (or an explicit unsupported marker, below).

**Determinism.** The corpus must be flake-free on loaded CI runners:

- Periodic machinery is made **inert** with long periods (365 d ticks;
  `post_period_ms=0` convention) so wall-clock never races a case.
- Materialize pending state through contract points instead of sleeping:
  bars flush on graceful stop; the **first periodic post fires immediately on
  start** (buffer values before `start`, assert payload 0).
- `sleep_ms` is reserved for explicitly wall-clock cases (bar rollover, sampled-bar partial posts);
  pair it with timing-immune invariants (`expect_bar_count_total`,
  alignment/monotonicity) rather than exact payload layouts where possible.
- Bar-alignment assertions only with periods that divide the 0001→1970 epoch
  offset (100/200/500/1000/2000/60000/3600000/86400000 ms) — .NET aligns in ticks,
  native in unix ms; they agree only there.
- Cross-language doubles: assert via `expect_bar_field` (tolerant) or pin
  binary-exact decimals (0.5/1.5/2.5); never assert long decimal tails as text.
- Multi-package retry order is not preserved (re-enqueue at tail) — use
  `expect_each_value_once`, not sequences. Inter-sensor flush order is
  unspecified — no cross-sensor index assertions.

**Versioning.** Fixtures carry a `v1` header comment. Evolution is additive:
new verbs and new fixtures do not bump the version. A breaking change to an
existing verb's args or semantics requires a corpus-wide version bump and a
simultaneous update of every driver — avoid; prefer a new verb.

## Driver contract

A driver maps verbs to public collector API calls against an in-proc
capturing sender, and renders captured payloads into the canonical text
above. **No test logic in drivers** (review checklist item): no
scenario-specific branches, no skipping, no result post-processing beyond the
canonical rendering. Unknown verbs must fail loudly (pinned by the
meta-suite) — never skip silently.

**Unsupported marker.** A driver that cannot yet implement a verb registers
it explicitly as unsupported so the run fails with a `TODO` count instead of
a generic unknown-verb error; the failure list is the port backlog. (No verb
is currently in that state — both drivers implement the full vocabulary.)

Mark such a verb in the driver source with the token `CONFORMANCE-UNSUPPORTED:
<verb> (#<issue>)`. The reference to a cpp-port issue is mandatory and enforced
in CI by [`scripts/conformance-unsupported-triage.ps1`](../../scripts/conformance-unsupported-triage.ps1)
(the `checklist-gate` job): an unsupported marker without a `#<issue>` fails the
build, so the backlog is always tracked, never silently accumulated.

## Meta-suite ("test the tests")

`collector/meta/` holds must-FAIL fixtures — deliberately wrong expectations
every driver is required to fail; a driver that passes one is broken. They
run through dedicated must-fail entry points and never join the regular
corpus. See [collector/meta/README.md](collector/meta/README.md).

## Parity-bug policy

A bug found in either implementation is assumed present in the other until
proven otherwise. Write the reproducing fixture FIRST (red on the affected
implementation), fix every implementation it is red on, keep the fixture
forever. Full flow: epic #1093, "Parity bug policy".
