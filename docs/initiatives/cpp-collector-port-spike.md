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

## Open Questions

- Should the native core own HTTP transport immediately, or should the first
  slice use a sender interface and add HTTP later?
- Should C++ wrapper methods throw on C ABI errors, return `std::expected`-like
  results, or provide both layers?
- Which package should be the first consumer target: Conan, NuGet native assets,
  or a local CMake install package?
- How much of the current `src/wrapper` API should remain source-compatible
  with future native C++ headers?
