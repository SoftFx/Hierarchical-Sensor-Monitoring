# Feature: Table Sensors in the Managed Collector

> Owner: collector | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: how an application declares table sensors in `HSMDataCollector`, adds rows, and how the collector buffers, registers, packs and delivers them (#1846, ADR-0021 points 5–7).

---

## Overview

A table sensor holds rows of a fixed schema: the time first, then value columns of doubles or 64-bit integers, every
value column nullable (ADR-0021 points 1–2). The application adds rows from any thread; the collector keeps them in
memory and, once per **table tick** (10 s by default), packs the rows of **all** its table sensors into one block of
the table block format (`aicontext/features/api/table-block/feature.md`, library `src/api/HSMTableBlocks`, zstd level 3,
no HSM column coders) and delivers it through the sender's table channel. A thousand table sensors make one message per
tick.

The first sensors carry the time, the bid and the ask:

```csharp
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

var collector = new DataCollector(new CollectorOptions { AccessKey = key, ServerAddress = "hsm.example" });

var options = new TableSensorOptions { Description = "EURUSD quotes", SensorLocation = SensorLocation.Product };
options.Columns.Add(TableColumn.Double("bid"));
options.Columns.Add(TableColumn.Double("ask"));
ITableSensor eurusd = collector.CreateTableSensor("quotes/EURUSD", options);

await collector.Start();
feed.OnQuote += q => eurusd.AddRow(q.ProviderTimeUtc, q.Bid, q.Ask);        // any thread, no allocation
feed.OnBatch += b => eurusd.AddRows(b.Times, b.Cells, b.Count);              // row-major cells, one call
double? noAsk = null; eurusd.AddRow(DateTime.UtcNow, 1.0841, noAsk);         // a cell without a value
await collector.Stop();                                                      // sends what it holds at once
```

**State of the steps (epic #1842).** Part 1 of this step (this document as written): the API, the buffer, the tick,
the registration on the session, packing and splitting, the bounded backlog, the answers to blocks, the stop drain.
The table connection that carries the blocks to the server is part 2: until it lands, the session sender answers every
block "not delivered", so blocks wait in the backlog (bounded, the oldest dropped and counted). The local file of
undelivered blocks (`TableSpoolDirectory`, opt-in) is part 3.

## Invariants

- **Additive API, both TFMs.** `ITableSensorCollector` is an optional capability next to `IDataCollector`, which stays
  unchanged (external implementations of it compile and bind as before); `DataCollector` implements it and the extension
  `TableSensorExtensions.CreateTableSensor(this IDataCollector, …)` dispatches to it. `IDataSender` is unchanged too:
  table support is the internal `ITableBlockChannel`, which only the session sender (and the test senders) implement.
- **The time column is implicit and first**, named `time` (`TableColumn.TimeColumnName`); no declared column may take
  that name. The schema is `time` + the declared columns; another shape is another sensor (path).
- **Creation throws only for a mistake of the call**: a null path or options, a path with no segment or with a
  backslash or a tab, a path longer than 1,024 UTF-8 bytes in its normalized form, no value column, an invalid or
  duplicate column name, the name `time`, a path already created with other columns. The same path with the same
  columns returns the existing sensor.
- **Creation never throws for a condition of the environment.** A collector that cannot send tables gives a sensor in
  state `Unavailable` with the reason in `StateReason`, reported once through `Collector errors`; its rows are dropped
  and counted. The conditions: the HTTP transport (also the automatic HTTP default on .NET Framework with
  `AllowUntrustedServerCertificate`), a custom `CollectorOptions.DataSender`, the session of the collector created first
  from the same options, a host on which no session can exist, and a collector that is stopping or disposed. The reason
  is decided when the collector is constructed: **an `Unavailable` sensor stays unavailable for the life of the
  collector**, whatever happens to the transport later; create the collector again.
- **The path is the server's normalized form**: the location prefix as for every sensor (`SensorLocation.Module`, the
  default, puts it under `ComputerName/Module`; `Product` at the root), then no empty segments and no white space around a
  segment (`TablePath.Normalize`, the server's `Split(RemoveEmptyEntries | TrimEntries)`). Registrations and blocks
  carry exactly that form.
- **A row of the wrong shape throws `ArgumentException`**, and nothing else ever throws from `AddRow` / `AddRows`: a
  cell count other than the column count, a double cell in an integer column (an integer cell in a double column is
  converted). Every other refusal returns `false` (a short count for `AddRows`), counts the rows in the sensor's
  `RowsDropped` and in the loss ledger of the run. That includes a buffer that cannot grow (out of memory on a
  constrained host): the buffer allocates every new array before it swaps any in, so a failed growth leaves it as it
  was, gives the reservation back and drops the rows of that call.
- **Nothing exists before the first table sensor.** The pipeline (and its zstd compressor) is made by the first
  `CreateTableSensor`; a host that never creates one gets no compressor, no tick, no task, no allocation and no log line
  of this feature, at Start, during the run and at Stop. A table sensor created while the collector runs starts the
  pipeline's run at once. A compressor that cannot be made makes the sensor `Unavailable` with the reason; it never fails
  `Start`.
- **A collector with a table sensor has a run from `Start` to `Stop`, also when none of its sensors can send** (an
  HTTP collector, a custom sender, no compressor): that run has the tick and the ledger and no delivery loop, and it
  never makes the compressor. The rows an `Unavailable` sensor refuses are lost rows, reported like every other loss.
- **Whether a run may start is decided under the collector's lifecycle lock** (`DataCollector._tableRunMayStart`): from
  the moment a `Start` has started the processor until a `Stop`, a `Dispose` or the rollback of that start begins.
  The status alone does not say it: the collector is still `Starting` while the rollback of a failed start stops its
  processor. A table sensor created during a rollback is created and starts no run; the rollback closes whatever run
  exists when it ends, so the collector never reaches `Stopped` with a tick or a delivery loop alive.
- **One object per run.** Everything a run owns is in its `TableRun`, made at `Start` and closed at the end of its stop:
  the cancellation, the packing and delivery gates, the backlog and the block in flight, the producer id and sequence,
  the bytes reserved by writers, the registration marks and the loss ledger. A sensor holds a reference to its current
  run; one call of `AddRow` reads it once and reserves, checks and appends against that one run. A run that ended
  refuses rows, packs nothing and enqueues nothing; a tick, a registration answer or a delivery of an old run can touch
  only its own retired run (each state change of a sensor checks, under the sensor's lock, that the sensor still belongs
  to that run). No generation numbers are compared.
- **`AddRows` is all or nothing**: the rows of one call are taken or dropped together.
- **The row time is the application's**: rows keep the order given, times may repeat and go backwards. A `DateTime` of
  kind Local is converted to UTC; Unspecified is taken as UTC.
- **Values travel bit for bit** (NaN, the infinities, -0.0); a null cell travels in the column's null map.
- **No silent loss** (rule 8): every dropped row is counted on its sensor and reported, see "Loss and its reports".
- **One block in flight, in sequence order.** The producer id is new at every `Start` (no local file yet) and the
  sequence starts at 0; a block that was not delivered is sent again with the same id and sequence, ahead of later blocks.
  A sequence is never reused; a block dropped by the bound leaves a gap.

## Primary Workflows

| # | Workflow | Initiator |
|---|---|---|
| 1 | Create a table sensor; registration on the next tick (and at once once the session is up) | collector integrator |
| 2 | Add rows (single, array, span, bulk) from any thread | application |
| 3 | Tick: report losses, register what waits, pack every registered sensor's rows, hand the blocks to delivery | collector scheduler |
| 4 | Deliver the backlog, stop and wait, and act on each answer | delivery loop |
| 5 | Stop: pack everything, deliver within the drain budget, drop and count the rest | `DataCollector.Stop` |

## API / Public Contracts

| Contract | Location | Notes |
|---|---|---|
| `ITableSensorCollector.CreateTableSensor(string path, TableSensorOptions options)` | `src/collector/HSMDataCollector/Core/ITableSensorCollector.cs` | also `DataCollector.CreateTableSensor` and the `IDataCollector` extension |
| `ITableSensor` | `PublicAPI/TableSensors/ITableSensor.cs` | `Path`, `Columns`, `State`, `StateReason`, `RowsAccepted`, `RowsDropped`; `AddRow(time, cells…)` for 1–4 cells, `AddRow(time, TableCell[])`, `AddRow(time, ReadOnlySpan<TableCell>)`, `AddRows(DateTime[], TableCell[], int rowCount)`, `AddRows(ReadOnlySpan<DateTime>, ReadOnlySpan<TableCell>)` |
| `TableSensorState` | same file | `Pending`, `Registered`, `Refused` (the server said no; final), `Unavailable` (this collector cannot send tables) |
| `TableCell` | `PublicAPI/TableSensors/TableCell.cs` | 16-byte value type; implicit from `double`, `long`, `double?`, `long?`; `TableCell.Null` |
| `TableColumn` | `PublicAPI/TableSensors/TableColumn.cs` | `TableColumn.Double(name)`, `TableColumn.Int64(name)`; `TimeColumnName = "time"` |
| `TableSensorOptions` | `Options/TableSensorOptions.cs` | `Columns`, `Description`, `KeepHistory` (sent as set; the server counts whole weeks, rounded up, and rows may stay up to a week longer), `SensorLocation` |
| `CollectorOptions.TableTickPeriod` | `Options/CollectorOptions.cs` | 10 s; at least 1 s |
| `CollectorOptions.MaxTableBufferBytes` | same | 64 MiB; rows waiting for the tick, 8 bytes per column per row, time included |
| `CollectorOptions.MaxTableBacklogBytes` | same | 64 MiB; packed blocks waiting for delivery |

**.NET Framework.** The span overloads put `System.ReadOnlySpan<T>` (assembly `System.Memory`) into the public
signatures of the net472 build for the first time; the array overloads need no `System.Memory` type in the host's code.
`System.Memory` was already a dependency of the net472 build (through `System.Text.Json`); `ZstdSharp.Port` raises it to
4.6.0. Checked on a clean SDK-style net472 consumer of the packed 4.1.0-preview: next to the redirects 4.0.1-preview
already needed (`Microsoft.Bcl.AsyncInterfaces` 8.0.0.0, `System.Runtime.CompilerServices.Unsafe`, now 6.0.1.0 instead of
6.0.0.0), the host needs `System.Memory` 4.0.2.0, `System.Buffers` 4.0.4.0 and `System.Numerics.Vectors` 4.1.5.0.
SDK-style projects generate them (`AutoGenerateBindingRedirects`); a classic project adds them to its `app.config`.

## Packaging

The package bundles `HSMTableBlocks.dll` next to `HSMSensorDataObjects.dll` (`PrivateAssets="all"` project reference and
`BuildOutputInPackage`), and depends on that library's packages directly: `ZstdSharp.Port` 0.8.8 (MIT, a managed port
of zstd: no native binaries, so the same assembly on Windows and Linux) and `System.IO.Hashing` 8.0.0 (MIT, Microsoft).
Projects that build against the collector project reference `HSMTableBlocks` explicitly, as they do the DTOs, and
`src/Directory.Build.targets` prefers an in-repo `HSMTableBlocks` over the package's copy.

## Key Files

| File | Purpose |
|---|---|
| `src/collector/HSMDataCollector/Tables/TablePipeline.cs` | Sensors, registration, tick, packing and splitting, backlog, delivery, stop, loss reports |
| `src/collector/HSMDataCollector/Tables/TableRun.cs` | One run: its gates, backlog, producer and sequence, reservations, registration marks; the loss ledger |
| `src/collector/HSMDataCollector/Tables/TableSensor.cs` | One sensor: schema, state, counters, the two row buffers swapped by a tick |
| `src/collector/HSMDataCollector/Tables/TableRowBuffer.cs` | Rows column by column, null maps on first use |
| `src/collector/HSMDataCollector/Tables/ITableBlockChannel.cs` | The internal seam to the sender: registration, put, limits; the answers (`TableBlockAck`, `TableRegistrationResult`) |
| `src/collector/HSMDataCollector/Tables/TablePath.cs` | The server's normalized path form |
| `src/collector/HSMDataCollector/Client/Session/SessionDataSender.Tables.cs` | The session's side: table registrations as a `commands` request of their own |
| `src/collector/HSMDataCollector.Tests/Tables/` | Unit tests (both TFMs: linked into the net8 IntegrationTests) |
| `src/collector/HSMDataCollector.Tests/Transport/TableRegistrationSessionTests.cs` | Registration over a real session |
| `tests/conformance/collector/table_sensor_contract.hsmtest` | The portable behaviour (rule 9) |

## Data Flow

1. **Add.** `TableSensor.AddRow` checks the shape (throws), reads the sensor's run once and asks whether rows are taken
   now (the sensor's state, the collector's data gate, whether that run still takes rows), reserves room in that run's
   buffer bound with a compare-and-swap that never overshoots (a request larger than the bound is refused before it
   touches the counter), and appends under the sensor's own lock after checking the same conditions again there, and
   that the sensor still belongs to the same run: a write that a refusal, the stop's final tick or a new run overtook
   gives its room back and is counted as dropped. A dropped row costs two counters; the words are written by the next
   tick.
2. **Tick** (`TablePipeline.TickAsync(run)`, on the collector's `CollectorScheduler`, which skips a tick while the previous
   one runs; a tick of a run that ended does nothing): reports the losses since the last tick, starts the registration of
   `Pending` sensors, then **packs**: each `Registered` sensor of the run has its buffer swapped for its spare under the
   sensor's lock (writers never wait for packing; a sensor that belongs to another run by now gives nothing), the
   rows are cut into pieces that keep to the block limits, the pieces are grouped into blocks (a table once per block),
   each block is written with one `TableBlockWriter` (reused) and checked against the frame limit: a block too large is
   split by tables, then by rows, and packed again. Blocks get the next producer sequence and go to the end of the
   backlog; the buffer room goes back. A pack observes its token (the run's; at stop the drain budget) before every
   compression pass: the rows it has not packed when the token fires are dropped and counted. The tick then signals the
   delivery loop. Nothing is reported while a gate or lock of the pipeline is held: reports are queued and written once
   the gates are released, so a host logger that calls back into the collector (even `Dispose`) cannot deadlock it.
3. **Delivery** (`DeliverAsync`, a loop of its own): the head of the backlog is put; the next block goes only after
   the answer to the one before. A block stays in flight, out of reach of the backlog bound, until its answer is
   applied: the removal from the backlog, the end of in-flight and the accounting are one step under the backlog's lock.
   An answer that came back is applied whatever the token says by then: a block the server stored is never counted as
   lost.
4. **Stop** (`FlushAsync`, inside `DataProcessor.StopWithFlushAsync`): runs on a thread of its own beside the value
   queues' drain under the same budget (one connection each, so the stop is not longer and packing never holds the value
   drain back). It takes the run out of the pipeline and begins its drain: from that moment the run's rows are refused
   and counted until the next `Start`, also while the collector is still `Stopping` for its value queues. It ends the
   tick and the loop, runs the final pack (after a pack of the run that is under way), delivers until the budget is
   spent or a block is not delivered, then closes the run (nothing of it is packed or enqueued again) and drops and
   counts the rest (the backlog, and rows of sensors still `Pending`). A start rollback does the same without
   delivering. An answer to a registration request of an earlier run changes nothing: every run registers its sensors
   itself.

### Registration

Every `Pending` sensor not already in a request goes into **one request of table registrations only** (wire contract:
an older server refuses type 11 in a way that must not take other registrations with it): `AddOrUpdateSensorRequest`
with `SensorType = TableSensor (11)`, `TableColumns` (`time`:Time first, then the declared columns),
`Description`, `KeepHistory` in ticks; no alerts, TTL or previous paths. It is sent when the run starts (once the
session is up), when a sensor is created while running, and at every tick while any sensor waits. On the session it is a
`commands` frame (`SessionDataSender.Tables.cs`):

| Answer | Sensor |
|---|---|
| 2xx, no error under the path | `Registered`: its rows go into the next block |
| 2xx, an error under the path | `Refused`, the server's words in `StateReason`; reported once, never sent again; its buffered rows are dropped and counted |
| 4xx for the request | `Refused` for every path of it (final, as for every registration) |
| 5xx, no ack, no session | stays `Pending`, rows kept within the buffer bound; sent again next tick, without limit |

Every `Start` registers every sensor again (`Registered` goes back to `Pending`), under a new producer id.

### Answers to a block

| Answer | What the collector does |
|---|---|
| `stored` | settled; each listed refused table: rows counted; `UnknownSensor` puts the sensor back to `Pending` (registered again; the rows of this block are lost); `NotATableSensor`, `SchemaIdMismatch`, `ColumnTypesMismatch`, `InvalidIdentifier` make it `Refused`; `SensorBlocked` (a sensor its user blocked on the server and can unblock there, so not final: the sensor stays
`Registered` and its later rows go out), `SensorCreatedAfterBlock` and reasons it does not know are counted and
reported only. More refused than listed (`refusedCount` > 100): reported |
| `duplicate` | settled, refusals as for `stored` (`refusedKnown: false` carries none) |
| `allRefused` | final: dropped, every row counted, the listed reasons applied |
| `400` | final: dropped, every row counted, reported as an invalid block |
| `503` / `500` / no answer | kept at the head, sent again with the same producer and sequence on a later tick; a `retryAfter` is honoured (capped at 5 minutes) |

## Loss and its reports

Every lost row is counted in its sensor's `RowsDropped` and in the **loss ledger of its run** (one per run, under no
gate). At every tick and at the end of the table drain the ledger reports what it gathered: one deduplicated line per
reason through `Collector errors`, and the rows to the `Queue overflow` diagnostic sensor under the queue
**"Table rows"** while that sensor takes values (from its start until it has published its last bar of the run; the
sensor refuses them after that, atomically with its last bar). Rows the sensor did not take stay in the ledger.

At the very end of the collector's stop (after the sensors stopped, the value drain and the table drain), **one line**
says what the ledger still holds: `Collector stop: N table row(s) were lost during this run and are not in the Queue
overflow sensor ...`: losses of a collector without that sensor, and losses after it published its last bar. There is no
other stop line about table rows. That line closes the ledger: a row refused after it (the collector is stopped) is
counted on its sensor only, and so is a row added before the first `Start`.

| Reason | When |
|---|---|
| not running | a row added while the collector is not Starting/Running/Stopping |
| buffer full | `MaxTableBufferBytes` reached |
| sensor refused / unavailable | a row added to such a sensor |
| backlog full | `MaxTableBacklogBytes` passed: the oldest blocks (never the one being sent) are dropped |
| refused by the server | refused tables of a block, `allRefused`, `400` |
| not delivered at stop | what a stop could not deliver |
| row too large / packing failed | a single row larger than a frame; a fault of the packer |
| buffer could not grow | an allocation failed while a sensor's buffer grew (out of memory) |

## Storage / Persistence

None in part 1: rows and blocks live in memory only, and a new producer id is taken at every `Start`. The opt-in local
file of undelivered blocks with its own bound comes with part 3.

## UI / Operator Visibility

`Collector errors` and the collector log (the lines above), `Queue overflow` with queue "Table rows", and on the
sensor itself `State`, `StateReason`, `RowsAccepted`, `RowsDropped`, which an application reads without the server.

## Dependencies

- Depends on: `src/api/HSMTableBlocks` (block writer, schema id), `src/api/HSMSensorDataObjects` (registration DTO),
  the collector session (`aicontext/features/api/wire-contract/feature.md`, "Table sensor registration" and
  "Table connection"), `CollectorScheduler`, `DataProcessor` (data gate, error channel, stop drain).
- Used by: applications with dense feeds (ticks).

## Tests

- Unit, net48 (the net472 build) and net8.0 (the net6.0 build, linked): `TableSensorTests` (API, shapes, unavailable
  sensors, registration answers, buffer and backlog bounds, splits, answers to blocks, stop, many threads against ticks),
  `TableRegistrationSessionTests` (registration over the scripted session: alone in its request, refusals final, faults
  retried). `TablePipelineRaceTests`: the orders of events found in review (a discard or a refusal racing a
  write, a stop racing a tick, an answer racing the bound, a stale registration answer), forced through internal hooks.
  `TableRunLifecycleTests`: the run as one object (a tick, a registration answer, a reservation of an earlier run never
  reach the next; an answer is applied after a cancellation), no pipeline without table sensors, a compressor that cannot
  be made, a failed buffer growth, the ledger and its one stop line, a logger that disposes the collector from a report,
  and a stress test of Start/Stop/Start/Stop with writers on every boundary.
- Conformance: `tests/conformance/collector/table_sensor_contract.hsmtest` through the .NET driver; the native driver
  marks the table verbs `CONFORMANCE-UNSUPPORTED (#1848)` and does not run the fixture until step 6.

## Known Issues / Limitations

- Part 1 has no table connection: blocks are not delivered by the session sender yet (part 2).
- No local file: a crash or a stop that cannot deliver loses what is held (counted); a restart takes a new producer id.
- `MaxTableBufferBytes` bounds the rows waiting for the tick (8 bytes per column per row). The arrays behind them are
  bounded apart from it: a buffer that grew past 256 rows gives its arrays up when a tick has packed it, so a quiet
  sensor keeps at most two buffers of 256 rows (4 KiB per column); the arrays of a busy sensor follow its rows. A busy
  sensor therefore allocates its arrays again every tick.
- Rows of refused tables beyond the first 100 of one acknowledgement are not in `RowsDropped`: the server lists at
  most 100 refusals and gives only the number of the rest (`refusedCount`), so the collector knows neither their tables
  nor their rows. The collector logs both numbers.

## Open Points

- Wire contract: an acknowledgement could carry the number of refused rows (or the indexes of all refused tables), so
  that a collector can count every lost row on its sensor also beyond the first 100 refused tables. A server change.
