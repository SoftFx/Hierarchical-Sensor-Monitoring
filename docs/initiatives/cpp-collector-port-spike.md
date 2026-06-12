# C++ Collector Port Spike

> Owner: collector/integrations | Created: 2026-05-29 | Status: spike

## Context

HSM already has a `src/wrapper` C++/CLI wrapper over the .NET `HSMDataCollector`.
That wrapper is useful as an API parity reference, but it is not a native C++
collector: it depends on CLR, targets Windows project files, and forwards calls
into the managed collector.

The spike goal is to learn what blocks a native C++ collector port and adjust
the .NET implementation where useful without changing the existing external
.NET API.

## Constraints

- Keep public .NET collector APIs and DTO compatibility stable.
- Treat `HSMSensorDataObjects`, wrapper headers, serialized payloads, and
  storage/API contracts as compatibility-sensitive.
- Prefer small .NET internal refactors that make the C++ model obvious over a
  parallel design that drifts from the managed collector.
- Use the existing C++/CLI wrapper headers as a compatibility inventory, not as
  the target runtime architecture.
- Make packaging experiments additive: Conan and NuGet can both exist if they
  serve different consumers.

## Candidate Shape

Use a layered native shape:

1. **Native core**: collector lifecycle, sensor registry, scheduler, queues,
   value batching, retry/backpressure policy, and transport-independent DTO
   model.
2. **C ABI boundary**: stable exported functions, opaque handles, plain structs,
   explicit ownership/free functions, and error codes/messages. This is the
   long-term interop boundary for C, C++, C#, Python, or other consumers.
3. **C++ wrapper**: RAII classes, strong types, templates/builders where useful,
   exceptions or `expected`-style errors as a C++ convenience layer over the C
   ABI.
4. **Packages**: Conan/vcpkg-style native consumption plus NuGet/native assets
   for .NET-adjacent consumers when useful.

## Spike Steps

1. Inventory the existing managed collector and `src/wrapper` public surface:
   lifecycle, instant sensors, bar sensors, rate sensors, function sensors,
   default sensors, options, alerts, status/comment behavior, and file sends.
   Done — see
   [`cpp-collector-port-functional-inventory.md`](cpp-collector-port-functional-inventory.md)
   (living checklist; tick items as the native core covers them).
2. Pick a vertical slice small enough to finish:
   `DataCollector` construction, `Start`/`Stop`, one instant sensor type, value
   enqueueing, JSON/request DTO serialization, and a fake/in-memory sender.
3. Define a draft C ABI for that slice:
   opaque `hsm_collector_t` / `hsm_sensor_t`, create/free, start/stop, add value,
   last error, and explicit string ownership rules.
4. Implement the slice in native C++ with tests, using the .NET behavior as the
   oracle for lifecycle and value semantics.
5. Build a thin C++ RAII wrapper on top of the C ABI and a tiny console consumer.
6. Compare behavior against the managed collector:
   path construction, status defaults, comments, timestamps, queue acceptance
   during lifecycle transitions, and error handling.
7. Record friction points and fix .NET internals only when they reduce semantic
   mismatch without public API changes.
8. Add one packaging experiment:
   Conan for native C++ consumers, NuGet native assets if .NET/native mixed
   consumption is still important.

## Expected Problem Areas

- **Lifecycle parity**: `Start`/`Stop`/`Dispose`, registering sensors before and
  after start, and values accepted during stopping.
- **Error model**: managed exceptions vs native error codes/messages; avoiding
  exceptions across ABI boundaries.
- **Ownership**: strings, sensor handles, collector handles, callbacks, and async
  work shutdown.
- **Templates vs ABI**: C++ templates are convenient but cannot be the stable
  binary boundary.
- **DTO serialization**: preserving managed wire format and enum/status values.
- **Scheduler behavior**: periodic send loops, bar collection, bounded shutdown,
  and callback exception isolation.
- **Transport**: HTTP client, retry policy, TLS options, and testable sender
  injection.
- **Platform sensors**: Windows performance counters and Linux `/proc` sensors
  should stay behind small source interfaces.
- **Packaging**: MSVC/GCC/Clang ABI differences, runtime libraries, static vs
  shared builds, symbols, and package metadata.

## First Spike Deliverable

Produce a native test project that can:

- Create a collector with fake sender settings.
- Create an integer instant sensor.
- Add a value with `Ok` status and optional comment.
- Flush or inspect the in-memory sent payload.
- Start and stop without leaking worker threads or handles.

The first deliverable does not need real HTTP transport, default sensors, alerts,
or complete wrapper parity.

## Spike Progress

### 2026-05-29: first native vertical slice

Added `src/native/collector_spike`, a standalone CMake project with:

- C ABI header: `include/hsm_collector/hsm_collector.h`.
- C++ RAII wrapper: `include/hsm_collector/hsm_collector.hpp`.
- Native implementation: `src/hsm_collector.cpp`.
- Self-contained test executable: `tests/hsm_collector_spike_tests.cpp`.

Implemented first-slice behavior:

- Create/destroy collector through opaque C handles.
- Start/stop collector.
- Create an integer instant sensor.
- Add integer values with status/comment.
- Drop values while stopped, matching the managed collector's data gating.
- Store sent payloads in an in-memory sender as JSON-like strings for inspection.
- Preserve managed status/type enum numeric values for the covered slice.
- Trim comments to the managed 1024-character limit.
- Expose C ABI errors as result codes; the C++ wrapper converts them to
  exceptions as a convenience layer.

Verification on Windows with Visual Studio CMake:

```powershell
cmake -S src\native\collector_spike -B src\native\collector_spike\build -G "Visual Studio 17 2022" -A x64
cmake --build src\native\collector_spike\build --config Debug --parallel
ctest --test-dir src\native\collector_spike\build -C Debug --output-on-failure
```

Result: `hsm_collector_spike_tests` passed.

Early findings:

- The C ABI should stay result-code based; C++ exceptions are useful only in the
  wrapper layer.
- Handle ownership needs to be explicit from the start. This slice uses
  collector-owned sensors with separate releasable sensor handles.
- The first native payload intentionally uses a simple JSON-like test string;
  production parity still needs a canonical serializer/wire-format comparison
  against `HSMSensorDataObjects`.

### 2026-05-29: split C++ tests into named parity cases

The first executable originally reported as one CTest test while running several
checks internally. It now registers separate CTest cases:

- `c_abi_before_start_drops_value`
- `c_abi_running_collector_stores_int_payload`
- `c_abi_duplicate_sensor_path_is_idempotent`
- `c_abi_long_comment_is_trimmed`
- `c_abi_invalid_arguments_return_errors`
- `c_abi_missing_payload_returns_not_found`
- `cpp_wrapper_creates_sensor_and_reads_payload`
- `cpp_wrapper_reports_invalid_path`
- `cpp_wrapper_start_twice_reports_invalid_state`

Result: 9/9 C++ spike tests passed.

Splitting the cases exposed an accidental double-free in the test helper RAII
handles, caused by implicit copying. The helper handles are now move-only. This
confirms that native ownership rules should be tested early and kept explicit in
both C ABI and C++ layers.

### 2026-05-29: shared conformance fixture for .NET and C++

Added a language-neutral conformance fixture:

- `tests/conformance/collector/instant_int_contract.hsmtest`

The fixture is action-based and is consumed by both:

- .NET adapter: `CollectorConformanceTests`.
- C++ adapter: `conformance_instant_int_contract` CTest case.

Added a single command for the shared contract pass:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\test-conformance.ps1
```

The first shared contract covers:

- Values added before `Start` are dropped.
- Running collector stores integer instant values.
- Values added after `Stop` are dropped.
- Duplicate sensor path registration is idempotent.
- Long comments are trimmed to 1024 characters.

The shared fixture immediately exposed a parity gap: .NET normalizes sensor paths
to `computer/module/path`, while the first native spike stored the raw path. The
native spike now applies the same path shape for the covered slice.

Verification:

- `.NET CollectorConformanceTests`: 5/5 passed.
- C++ `conformance_instant_int_contract`: passed.
- Full C++ spike CTest suite: 10/10 passed.

### 2026-05-29: expand shared conformance toward one test matrix

The conformance runner now discovers all collector `*.hsmtest` fixtures and runs
them through both the managed collector and the native spike. Added shared
fixtures:

- `tests/conformance/collector/lifecycle_int_contract.hsmtest`
- `tests/conformance/collector/stress_int_contract.hsmtest`

The shared DSL now covers:

- Creating one or many integer instant sensors.
- Sequential and parallel value ingestion.
- Start, stop, restart, and registration before start / during running / after
  stop.
- Bounded stress profiles suitable for normal PR validation.
- Shared payload assertions across every emitted value.

Language-specific unit tests remain for surfaces that are not shared contracts:
.NET internals such as queue implementation and C++ details such as C ABI
invalid handles, ownership, and RAII wrapper behavior.

Verification:

- Shared conformance script: .NET 12/12 passed, C++ conformance 3/3 passed.
- Full C++ spike CTest suite: 12/12 passed.
- Full .NET collector unit suite: 190 passed, 9 skipped.

### 2026-05-30: broaden shared contracts beyond smoke coverage

Expanded the shared test matrix from 12 managed conformance cases to 38 managed
cases, all driven by the same `*.hsmtest` files that the C++ spike consumes.
Added fixtures:

- `tests/conformance/collector/value_int_contract.hsmtest`
- `tests/conformance/collector/cardinality_int_contract.hsmtest`

New shared coverage includes:

- Integer boundary values: zero, negative, `int.MinValue`, `int.MaxValue`.
- Sensor status enum wire values: `OffTime`, `Ok`, `Warning`, `Error`.
- Path composition with punctuation and spaces.
- Comment handling, including JSON escaping and long-comment trimming.
- Payload ordering for sequential sensor-major writes.
- Restart behavior, idempotent stop, and stopped values not flushing after
  restart.
- Bounded cardinality and duplicate-path load.
- Wider sequential and parallel stress profiles.

The remaining managed tests are not yet portable because the native spike does
not yet implement their behavioral surface: double/string/bool/version/time
instant sensors, last-value sensors, bar/rate/function/file sensors, default
sensors, HTTP transport chaos, queue overflow internals, resource leak probes,
and scheduler-specific implementation tests. As each native surface is added,
the matching managed test scenarios should move into `tests/conformance` first.

Verification:

- Shared conformance script: .NET 38/38 passed, C++ conformance 5/5 passed.

### 2026-05-30: make the native spike test suite conformance-only

The native CTest suite now registers only tests backed by shared
`tests/conformance/collector/*.hsmtest` fixtures. The earlier C++-only C ABI and
RAII wrapper checks were removed from the runnable test matrix so the port is
validated by the same behavior contracts as the managed collector.

Going forward, new port tests should be added as shared fixtures first. A
language-specific test is acceptable only for build mechanics or API binding
plumbing, and should not be counted as collector behavior coverage.

### 2026-05-30: move primitive instant sensors into shared contracts

Added a native slice for boolean, double, and string instant sensors through the
C ABI so their behavior can be verified by the same conformance fixture as the
managed collector:

- `tests/conformance/collector/instant_mixed_contract.hsmtest`

The shared instant sensor contract now covers:

- `bool`: true/false payloads, status values, stopped-value drop behavior.
- `double`: fractional, negative, and zero payloads.
- `string`: basic, empty, escaped JSON-special payloads, and comment trimming.

This raises shared conformance coverage from 38 managed cases to 48 managed
cases, with 6 native CTest entries, all backed by shared fixtures.

Verification:

- Shared conformance script: .NET 48/48 passed, C++ conformance 6/6 passed.

### 2026-05-30: move last-value sensors into shared contracts

Added a native last-value sensor slice and a shared fixture:

- `tests/conformance/collector/last_value_contract.hsmtest`

The shared last-value contract covers:

- `int`, `bool`, `double`, and `string` last-value sensors.
- Remembering multiple `AddValue` calls and flushing only the latest value on
  `Stop`.
- Default-value flush on `Stop`.
- Values added before `Start` and while stopped being retained for the next
  stop flush, matching managed last-value semantics.
- Status propagation and long-comment trimming at flush time.

Verification:

- Shared conformance script: .NET 56/56 passed, C++ conformance 7/7 passed.

### 2026-05-30: move enum sensors into shared contracts

Added enum instant sensor coverage:

- `tests/conformance/collector/enum_contract.hsmtest`

The shared enum contract covers zero, positive, and negative enum values,
status propagation, stopped-value drop behavior, and the `SensorType.EnumSensor`
wire value (`10`).

The shared test exposed a managed bug: `CreateEnumSensor(string path, string
description)` routed through the generic integer instant-sensor factory and sent
`SensorType.IntSensor` (`1`) instead of `SensorType.EnumSensor` (`10`). The
overload now delegates to the enum-specific overload.

Verification:

- Shared conformance script: .NET 60/60 passed, C++ conformance 8/8 passed.

### 2026-05-31: add unified mixed stress conformance

Added the primary shared stress fixture:

- `tests/conformance/collector/stress_mixed_contract.hsmtest`

Unlike the earlier integer-only stress fixture, this one loads the currently
portable mixed instant surface together: `bool`, `int`, `double`, `string`, and
`enum`. The fixture covers:

- Parallel mixed writes across 24 sensor sets and 12 workers.
- Sequential mixed fanout across 32 sensor sets.
- Drop-after-stop behavior under mixed stress.
- Exact total sent counts and per-type payload counts.

This fixture should be the default stress gate for every collector port. Larger
soak profiles can be added separately as opt-in fixtures, but the PR profile
must stay shared and bounded.

### 2026-05-31: harden mixed stress against duplicate path/type bugs

Extended `tests/conformance/collector/stress_mixed_contract.hsmtest` with a
duplicate-path registration stress case. The case pre-registers integer sensors,
then concurrently attempts to register `bool`, `double`, `string`, and `enum`
sensors on the same paths. Conflicting sensor types must fail at creation time,
leaving the original integer sensors usable.

The stress case exposed two related registration holes:

- Native C++ keyed sensors only by path and returned an existing handle even
  when the requested type differed.
- Managed storage rejected most type conflicts only accidentally through casts,
  while `int`/`enum` conflicts could reuse the wrong sensor because both expose
  `IInstantValueSensor<int>`.

Both collectors now make the invariant explicit: a duplicate path can reuse an
existing sensor only when the registered sensor type matches.

Verification:

- Shared conformance script: .NET 64/64 passed, C++ conformance 9/9 passed.

### 2026-05-31: start stress review loop

Started a subagent-backed stress review loop with three focused review tracks:

- Native lifecycle/concurrency.
- Managed/native contract gaps.
- C ABI/native API safety.

The first shared-semantics batch added coverage for:

- Idempotent `Start`: calling `Start` twice leaves the collector running and
  able to send subsequent values. Native now matches the managed collector.
- Sensor identity includes both wire `SensorType` and instant-vs-last-value
  behavior. Managed storage now makes this invariant explicit via internal
  sensor identity metadata; native already rejects mismatched last-value mode.

This keeps the port aligned with the original collector instead of letting the
C++ implementation drift into its own semantics.

Verification:

- Shared conformance script: .NET 67/67 passed, C++ conformance 9/9 passed.
- Full native CTest: 9/9 passed.
- Full managed collector tests: 245 passed, 9 skipped.

### 2026-05-31: reach 10 bug-finding stress/regression tests

Continued the subagent-backed loop until the branch had 10 named important
tests that exposed P1/P2 bugs or drift in either collector. The second batch
added native-only C ABI safety coverage and shared path normalization coverage:

- Native invalid calls clear caller-owned out parameters.
- Native instant and last-value sensor handles reject `AddValue` after their
  collector has been destroyed.
- Native missing sent-json lookup sets a fresh `last_error`.
- C++ wrapper missing sent-json lookup throws a meaningful message.
- Leading/trailing slashes in sensor paths are normalized consistently.
- Slash-only paths are rejected explicitly.

Fixes kept shared semantics aligned across the .NET collector and native port:
managed storage now rejects empty/slash-only paths before building full paths,
and native path construction trims slash boundaries like the managed
`DefaultPrototype.BuildPath` helper.

Verification:

- Shared conformance script: .NET 69/69 passed, C++ conformance 9/9 passed.
- Full native CTest: 13/13 passed.
- Full managed collector tests: 247 passed, 9 skipped.

### 2026-06-11: crash-isolation harness and the first cross-cutting invariant (#1103)

Fixed the #1102 A1/A2 host-crash vectors in the managed collector test-first:

- A1: a throwing `onError`/`ExceptionThrowing` callback escaped the scheduler's
  async-void dispatch (`CollectorScheduler.ExecuteQueuedTask`) and killed the
  host process. Now isolated at three layers: dispatch catch-all, guarded
  `onError` invocation, per-subscriber isolation in `SensorBase.HandleException`.
- A2: `ProcessEventListener.OnEventWritten` threw on malformed EventCounters
  payloads (null `EventName`, missing `Name`/`Mean` keys, non-double `Mean`).
  The callback body is extracted into an internal, directly testable method and
  the whole callback is guarded. Empirical finding recorded along the way: on
  net6 the in-proc `EventSource` dispatch swallows listener exceptions
  (`ThrowOnEventWriteErrors=false`), so in-proc A2 was silent counter loss
  rather than process death; the guard removes the reliance on that runtime
  behavior entirely.

Because a host-process crash cannot be asserted from inside the test process,
crash isolation is covered by a process-isolated harness instead of `.hsmtest`
fixtures: `HSMDataCollector.CrashTests.Host` (console host wiring deliberately
throwing callbacks) spawned by `CollectorCrashIsolationTests`, plus an
in-process host-callback adversarial matrix in `CollectorAdversarialTests`.
CI lane: `.github/workflows/collector-unit-tests.yml`.

### 2026-06-11: #1102 reliability wave beyond the crash vectors

Fixed test-first (26 new unit tests, suite at 343 green):

- **E1**: per-process performance-counter categories bind instances by PID and re-validate the
  binding on every read (`ProcessAwarePerformanceCounter` behind the new
  `IPerformanceCounterSource` seam); name-only categories prefer exact instance-name matches.
- **B1**: the scheduler worker restarts with backoff on unexpected exceptions instead of dying
  silently. **B2**: `GetInstanceNames()` / `GetServices()` are bounded by `BoundedBlockingCall`.
- **C1**: `MaxFileSizeBytes` clamped to a 128 MB ceiling; duplicate file-buffer copy removed.
- **E2**: rate sensor divides by actual elapsed time (monotonic clock), not the configured period.
- **E4**: bounded connection lifetime on both TFMs forces periodic DNS re-resolution.
- **E5**: `DateTimeKind.Local` timestamps normalized to UTC at the send boundary (DTO untouched).
- **C2** (closed after merging master's #1090/#1091 queue followups): the BuildDate mirror cannot
  be dropped — it provides the head-peek that `Channel<T>` lacks — so channel and mirror updates
  are now one atomic step under `_mirrorLock`. The orphan-tick desync was deterministically
  reproduced by `QueueMirrorConsistencyTests` (hot producer/consumer at the near-empty boundary)
  before the fix.

Port-relevant invariants from this wave: bind process-scoped OS metrics by process id (re-validated
per read), bound every OS call that has no timeout of its own, divide rates by measured elapsed
time, normalize timestamps to UTC before serialization, and bound transport connection lifetimes
for DNS re-resolution. Out of scope by decision: D2 (#1096), E3 (#1099), D1/D3/D4 (architectural
trade-offs).

### 2026-06-12: bar aggregation + data-loss conformance (both languages)

Closed the two largest conformance gaps — bar sensors and the queue/transport
data-loss surface — with 42 new shared cases in 7 fixtures, implemented in both
harnesses at once:

- `tests/conformance/collector/bar_int_contract.hsmtest` (12): min/max/mean/
  first/last/count exactness, banker's-rounding pins for the int mean (2,3 → 2;
  3,4 → 4), large-count mean (sum > int32), values-before-Start accumulate,
  empty bar emits nothing, stop→restart no resend, dispose-no-flush,
  8×250 parallel adds aggregate exactly, multi-sensor count-total.
- `bar_double_contract.hsmtest` (8): binary-exact aggregation, precision
  rounding (away-from-zero), NaN/±Infinity silently skipped without corrupting
  the bar.
- `bar_partial_contract.hsmtest` (9): `AddPartial` weighted-mean merge and
  min/max union, strict int validation vs the double epsilon tolerance
  (`max(1e-12, |max-min|*1e-9)`) — just-inside accepted, outside rejected.
- `bar_rollover_contract.hsmtest` (3, the only wall-clock cases): roll-on-add
  past CloseTime, no value lost across the boundary (count-total invariant),
  idle periods emit no empty bars, open/close aligned to the period in unix-ms.
- `queue_overflow_contract.hsmtest` (3): FIFO eviction keeps the newest suffix
  (150 into capacity 100 → exactly 50..149 in order), exact capacity = no
  eviction.
- `sender_retry_contract.hsmtest` (3): failed sends re-enqueue at the tail and
  retry with no loss and no duplicates (single-package keeps order; the
  multi-package case pins set-equality because re-enqueue rotates order); a
  send failure during the stop flush drops the remainder and Stop completes.
- `flush_contract.hsmtest` (4): graceful Stop drains everything pending (60 s
  collect period proves the flush did it, not the dispatcher), across packages,
  in order; restart accepts new values.

Production change (managed, deliberate): `BarMonitoringSensorBase.StopAsync`
now flushes a non-empty partial bar before stopping (previously the in-progress
bar was silently lost at shutdown). The roll happens only on a confirmed send
(the same `if (TrySendValue()) BuildNewBar()` shape as `CheckCurrentBar`), and
a new `DisposeAsyncCore` keeps sensor disposal non-flushing (mirrors
`LastValueSensorInstant`). Default system bar sensors now publish their final
partial bar at collector shutdown; the server already merges partial bars by
OpenTime.

Native spike grew the matching machinery:

- Bar sensors (`MonitoringBar` in `hsm_collector.cpp`): same accumulation,
  partial-merge, and validation math; int mean via `std::nearbyint`
  (round-half-to-even — `std::round` would diverge from C#), double fields via
  `std::round(v*10^p)/10^p` (away-from-zero); roll-on-add + flush-on-stop with
  the snapshot-under-lock/publish-outside-lock shape — the bar-roll invariant
  from the sensors feature doc now has a C++ analogue.
- Bounded FIFO send queue: `hsm_collector_options_t` gains `max_queue_size` /
  `max_values_in_package` / `package_collect_period_ms` (0 ⇒ 20000/50/20 ms,
  the managed conformance defaults), worker-thread dispatch, oldest-first
  eviction on the enqueueing thread, re-enqueue-at-tail on injected failure
  (`hsm_collector_set_send_fail_next`), graceful-stop drain that drops the
  remainder on failure. Dispatch is now asynchronous, so the C++ harness's
  `expect_sent_count` polls with a deadline (exact-equality, like the managed
  `WaitForCountAsync`) and the formerly synchronous native payload asserts wait
  for the first dispatch.

Canonical bar payload (both harnesses emit/parse the same shape; numeric
asserts compare with relative tolerance 1e-9 so double formatting may differ):
`{"Type":4|5,"Path":..,"Min":..,"Max":..,"Mean":..,"First":..,"Last":..,"Count":..,"OpenTimeMs":..,"CloseTimeMs":..,"Status":1,"Comment":""}`.
Alignment asserts are restricted to periods that divide the 0001→1970 offset in
ms (100/200/500/1000/2000/60000/3600000 all do), so tick-space (C#) and
unix-ms (C++) alignment agree.

Out of conformance scope by decision: periodic partial-bar posting
(`PostDataPeriod`; fixtures pin the post period inert), retry-older-than-head
(#1090 — not orchestrable through the action protocol, stays managed-only in
`QueueMirrorConsistencyTests`), dispose-without-stop drop semantics (the mock
sender cannot distinguish graceful vs terminal flush).

Verification:

- .NET conformance 139/139 (97 prior + 42 new), full managed suite 432/432
  (9 skips pre-existing), 5 repeat runs green.
- C++ ctest 40/40 (24 native + 16 conformance fixtures);
  `conformance_bar_rollover_contract` flake-screened 50 consecutive runs green.

### 2026-06-12 (follow-up): shutdown boundedness under a dead/hung transport

Pinned the contract that collector `Stop()` must return within a small bounded
time even when the server is unreachable — the collector must never lock the
host service's restart; pending data (including a flushed partial bar) is
dropped instead.

Managed audit found one real hole and fixed it: `QueueProcessorBase.FlushAsync`
awaited `TryDispatchOneAsync(token)` unbounded, so an `IDataSender` that
ignores cancellation and hangs **for the first time on the stop-flush dispatch
itself** (the run-loop side was already guarded by `StopAsync`'s WhenAny
timeout) would hang `Stop()` forever. The flush now races each dispatch against
the flush token and abandons the in-flight send when the timeout fires (fault
observed, loss logged). The real HTTP client respects cancellation, so this
only affected custom senders.

New tests:

- `CollectorStopBoundednessTests` (managed): hung-but-cancellation-respecting
  sender with pending values + pending partial bar; cancellation-ignoring
  sender hung in the run loop (flush skipped); cancellation-ignoring sender
  hanging first at flush (fails without the FlushAsync fix). All assert
  `Stop()` returns well under a generous bound (actual ≈1 s with
  RequestTimeout=1 s).
- Conformance (`flush_contract.hsmtest`, both languages): new actions
  `set_sender_hang` (sender blocks until the stop path cancels it — dead
  transport) and `stop_expect_under_ms|bound`; cases
  `stop_with_hanging_sender_is_bounded_and_drops_pending` and
  `stop_with_hanging_sender_drops_pending_bar` (stop under 8 s, 0 delivered).
- Native spike: `hsm_collector_set_send_hang` + in-flight send cancellation —
  `StopWorker` cancels hung sends before joining, and the stop drain treats a
  cancelled hung send as a failed send and drops the remainder, so native stop
  is bounded by construction.

Bound tightened by decision: the graceful stop wait is capped at **5 s**
regardless of `RequestTimeout` (`ShutdownMode.StopWaitTimeout`, previously the
full `RequestTimeout` — 30 s by default). Rationale: the collector must never
hold the host service's restart for a transport timeout; losing pending data at
stop is explicitly acceptable. Terminal dispose stays capped at 1 s per queue,
and the stop-flush ceiling was already 5 s, so the whole graceful path now
shares one 5 s upper bound per transport-facing wait. Pinned by
`Stop_with_default_request_timeout_is_capped_at_five_seconds_per_hung_queue`.

Verification: managed conformance 141/141, full managed suite 438/438 green
(9 skips pre-existing); C++ ctest 40/40.

### 2026-06-12: rate / function / file sensor conformance (both languages)

Closed the last sensor-kind conformance gaps with 15 new shared cases in 3
fixtures, implemented in both harnesses at once:

- `tests/conformance/collector/rate_contract.hsmtest` (6): zeros while idle,
  eventually-positive after adds, sticky status/comment, invalid-status and
  NaN increments silently dropped, **Stop does NOT flush the pending sum**
  (deliberate: a partial-window rate is alert-noise risk — data preservation
  at stop applies to bars, not rates). Rate = sum / measured elapsed seconds;
  the exact value is timing-dependent, so the portable cases assert invariants
  only and the elapsed-time math stays pinned by language-local unit tests
  (#1102-E2 on the managed side).
- `function_contract.hsmtest` (5): the no-params function posts its constant,
  the values-function receives a SNAPSHOT of the buffered sliding window
  (sum 1..5 = 15 — the buffer is not drained between posts), oldest values
  evicted past max_cache_size (cap 3 → 3+4+5 = 12), values may be buffered
  before Start.
- `file_contract.hsmtest` (4): UTF-8 string-content publish round-trips with
  Type 6 + Name/Extension from options, values before Start dropped, null
  content silently ignored, and file payloads dispatch PROMPTLY — push-driven,
  not gated by the package collect period (the C# file queue wakes on enqueue,
  unlike the batched data queue; the native worker gets an explicit kick).
  Disk-based `SendFile` stays language-specific (not portable).

Contract pinned along the way: **the first periodic post fires immediately on
Start** (managed schedule due time = 0), then every post period — the fixtures
exploit this for determinism (long inert period + assert only the immediate
initial post; e.g. values-function cases buffer before Start and read payload
0 exactly).

Native spike grew a periodic scheduler: a per-collector scheduler thread
(~10 ms granularity, started on Start, joined before the stop flush) ticks
periodic sensors — rate (monotonic-clock elapsed division, sticky
status/comment, baseline reset on restart), int function (C callback
`hsm_int_function_t`), values-function (sliding-window buffer + snapshot
callback `hsm_int_values_function_t`) — plus a string-content file sensor.
Sensor locks stay strictly outside the collector lock (snapshot-then-tick),
the same one-way order as the bar path.

Verification: managed conformance 156/156 (×3 repeat runs), full managed
suite 453/453 green (9 skips pre-existing); C++ ctest 43/43;
rate/function/file fixtures flake-screened 30 consecutive runs green.

## Cross-Cutting Port Invariants

Behavioral invariants every port must uphold that are NOT expressible as
`tests/conformance/collector/*.hsmtest` fixtures. Each needs a port-native
equivalent of the managed guard listed next to it.

| Invariant | Managed guard | Port obligation |
|---|---|---|
| **Callbacks never crash the host.** Every host-supplied callback surface — scheduler `onError`, the `ExceptionThrowing` event, loggers, lifecycle listeners, runtime/event-stream callbacks (`ProcessEventListener`) — is isolated; a throwing callback may neither kill the process nor break the component that invoked it (other subscribers still fire, the scheduler worker and sensor loops keep running). | `HSMDataCollector.CrashTests.Host` + `CollectorCrashIsolationTests` (process-isolated, #1102 A1/A2), host-callback matrix in `CollectorAdversarialTests`, `ProcessEventListenerTests` | The native core must wrap every user-callback invocation (error callbacks, log sinks, lifecycle observers) and prove it with its own process-isolated crash harness — exceptions must not cross the C ABI boundary. |

## Open Questions

- Should the native core own HTTP transport immediately, or should the first
  slice use a sender interface and add HTTP later?
- Should C++ wrapper methods throw on C ABI errors, return `std::expected`-like
  results, or provide both layers?
- Which package should be the first consumer target: Conan, NuGet native assets,
  or a local CMake install package?
- How much of the current `src/wrapper` API should remain source-compatible
  with future native C++ headers?
