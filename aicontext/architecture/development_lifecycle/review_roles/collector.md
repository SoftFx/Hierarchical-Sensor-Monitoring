# Collector SDK Review Roles

## Collector Lifecycle Reviewer

Focus:

- `DataCollector`, `DataProcessor`, lifecycle state transitions, start/stop/dispose ordering;
- sensor registration, initialization, start/stop behavior, and duplicate path handling;
- background queues, retry behavior, cancellation, disposal, and resource ownership;
- thread-safety for concurrent `Start`, `Stop`, `Dispose`, sensor creation, and sending values;
- compatibility with existing collector consumers on supported target frameworks.

Must read:

- changed files under `src/collector/HSMDataCollector/Core`, `Sensors`, `SyncQueue`, `Threading`, `Client`, and `Exceptions`;
- changed collector tests under `src/collector/HSMDataCollector.Tests`;
- public interfaces under `PublicInterface`, `PublicAPI`, options, and DTO usage when touched;
- relevant docs under `docs/test`, `ai-docs`, `wiki-draft`, or `aicontext/features/collector` when present.

Output:

- lifecycle/race findings with an execution path;
- disposal or leak risks for timers, queues, HTTP clients, and sensors;
- backwards-compatibility risks for collector public API behavior;
- missing focused tests for concurrent lifecycle, transport failure, cardinality, resource leaks, or scheduler behavior.

---

## Sensor Behavior Reviewer

Focus:

- instant, rate, bar, file, function, service, Windows, and Unix sensor behavior;
- timing semantics such as post period, bar period, due time, and default value handling;
- error propagation through `HandleException`, deduplication, and collector logging;
- sensor path construction, options copy/fill behavior, display unit and sensor type consistency.

Must read:

- changed sensor base classes and default sensor implementations;
- `HSMSensorDataObjects` request/response types touched by the PR;
- tests covering default sensors and adversarial collector behavior.

Output:

- behavior mismatches between sensor options, request DTOs, and emitted values;
- timing or duplicate-send risks;
- platform-specific Windows/Unix assumptions;
- missing tests for default sensors, option copying, and failure handling.
