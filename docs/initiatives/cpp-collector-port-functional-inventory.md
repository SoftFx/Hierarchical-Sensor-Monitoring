# C++ Collector Port — Coverage Tracker (Spike Step 1)

> Owner: collector/integrations | Created: 2026-06-10 | Status: living checklist
> Companion to [`cpp-collector-port-spike.md`](cpp-collector-port-spike.md).

The **functional content** lives in the maintained canonical docs under
[`aicontext/`](../../aicontext/README.md) — update those in the same work cycle
as any collector behavior change. This file only tracks which areas the native
C++ port has covered; tick boxes as slices land, and link the native
implementation/tests next to each tick.

Legend: **[wire]** = byte-for-byte compatibility required. **[decide]** = port
decision needed, not automatically in scope.

## Port coverage

- [ ] **Construction & options** — [`aicontext/features/collector/public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md) (§Construction, §CollectorOptions); `IDataSender` seam
- [ ] **Lifecycle** — [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md) (§Lifecycle API) + [`overview.md`](../../aicontext/features/collector/overview.md) (state machine, gates, registration phases, dispose-vs-stop race, event ordering)
- [ ] **Registration semantics** — [`overview.md`](../../aicontext/features/collector/overview.md) (§Sensor registration): path dedup, type-conflict throw, MaxSensors, dynamic start
- [ ] **Sensor creation surface** — [`public-api/feature.md`](../../aicontext/features/collector/public-api/feature.md) (§Sensor creation): every factory + interface + fluent builders
- [ ] **Sensor mechanics** — [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md): taxonomy, validation rules (NaN/Infinity, 1024-char comments, partial-bar tolerance), lifecycle epoch, bar roll invariant, bar UTC alignment, rate CAS, file limits, function cache
- [ ] **Options / prototypes / paths** — [`sensors/feature.md`](../../aicontext/features/collector/sensors/feature.md) (§Options & path model)
- [ ] **Alert DSL** — [`alerts/feature.md`](../../aicontext/features/collector/alerts/feature.md)
- [ ] **Default sensors: Windows** — [`default-sensors/feature.md`](../../aicontext/features/collector/default-sensors/feature.md) (perf counters, WMI, registry, EventLog, ServiceController, disk fan-out, prediction algorithm)
- [ ] **Default sensors: Unix** — same doc (/proc parsers, DriveInfo); **[decide]** whether to close Unix-vs-Windows gaps natively
- [ ] **Module & diagnostic sensors** — same doc (alive/version/errors/product-version/service-commands, queue stats)
- [ ] **Queues & pipeline** — [`data-pipeline/feature.md`](../../aicontext/features/collector/data-pipeline/feature.md): four queues, overflow/retry (#1088/#1090 rules incl. FIFO-head BuildDate mirror + shutdown bypass), ShutdownMode matrix, drain order, diagnostics suppression, EnqueueResult semantics
- [ ] **HTTP transport** — [`http-client/feature.md`](../../aicontext/features/collector/http-client/feature.md): endpoints, headers, TLS opts, Polly config, CancelPendingRequests contract; **[decide]** whether to reproduce or fix the 4xx/5xx retry gap
- [ ] **Scheduler** — [`scheduling/feature.md`](../../aicontext/features/collector/scheduling/feature.md): timer wheel, monotonic clock, onError contract, ScheduledTaskHandle, catch-up
- [ ] **Error handling / dedup / logging** — [`error-handling/feature.md`](../../aicontext/features/collector/error-handling/feature.md): isolation rules, routing map, MessageDeduplicator (zero-window rule)
- [ ] **[wire] Wire contract** — [`aicontext/features/api/wire-contract/feature.md`](../../aicontext/features/api/wire-contract/feature.md): enum numeric values, DTO shapes, JSON conventions, endpoints — byte-for-byte
- [ ] **[decide]** Obsolete surface (sync `Initialize*`, legacy func-sensor getters, `ValuesQueueOverflow`) — recommend NOT porting; see `public-api/feature.md` §Obsolete
- [ ] **[decide]** Wrapper parity gaps (TimeSpan/Version/Enum/Counter sensors, service-commands, lifecycle listeners, history queries are absent from `src/wrapper` today)

## Cross-cutting invariants (gate for every slice)

Collected at the end of each aicontext doc; the short list every port slice must satisfy:

1. Values before Start / after Stop silently rejected; no exceptions to producers.
2. Start/Stop/Dispose idempotent and race-safe; exactly one ToStopped per cycle.
3. Path dedup transparent; type conflict throws.
4. Validation before enqueue (NaN/Infinity/null/1024-char comments/bar consistency).
5. Bars never roll without a confirmed send; windows UTC-epoch aligned.
6. Stale timer callbacks invalidated by lifecycle epoch.
7. FIFO at-least-once queues; retry-forever + overflow backstop; newest-data-wins (#1088/#1090); graceful stop flushes, terminal dispose stays bounded.
8. Diagnostics suppressed after drain boundary; overflow telemetry exempt.
9. Scheduler: monotonic clock, catch-up, onError, loop never dies.
10. Logger/listener exceptions always swallowed.
11. Wire: enum values, JSON names/formats, endpoints frozen.
