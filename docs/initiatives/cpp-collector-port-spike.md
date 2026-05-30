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

## Open Questions

- Should the native core own HTTP transport immediately, or should the first
  slice use a sender interface and add HTTP later?
- Should C++ wrapper methods throw on C ABI errors, return `std::expected`-like
  results, or provide both layers?
- Which package should be the first consumer target: Conan, NuGet native assets,
  or a local CMake install package?
- How much of the current `src/wrapper` API should remain source-compatible
  with future native C++ headers?
