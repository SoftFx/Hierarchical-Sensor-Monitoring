# Feature: Table Sensors in the Managed Collector

> Owner: collector | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: how an application declares table sensors in `HSMDataCollector`, adds rows, and how the collector buffers, registers, packs and delivers them over the table connection of its session (#1846, ADR-0021 points 5–7).

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

**State of the steps (epic #1842).** This document is parts 1 and 2 of this step: the API, the buffer, the tick, the
registration on the session, packing and splitting, the bounded backlog, the answers to blocks, the stop drain
(part 1), and the table connection that carries the blocks to the server (part 2, "The table connection" below). The
local file of undelivered blocks (`TableSpoolDirectory`, opt-in) is part 3: until then a block that is not delivered
lives in memory only.

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
- **The table connection follows the session.** It is made with the first table sensor and opened only while the
  session is up and after a table registration of the run was accepted; it names that session's `sessionId`, and it
  is replaced when the session is. One attempt to open it at a time, with the session's backoff.
- **A block leaves the backlog only by an answer of the server** (or by the bound, or by the stop): no ack, a close of
  the connection (`1013` included), a refused upgrade and a lost session all leave it where it is, and it goes again
  with the same producer id and sequence.
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
| `src/collector/HSMDataCollector/Client/Session/SessionDataSender.Tables.cs` | The session's side: table registrations as a `commands` request of their own; makes the table connection with the first table sensor |
| `src/collector/HSMDataCollector/Client/Session/TableConnection.cs` | The table connection: the loop that keeps it, the upgrade, the hello and the probe, a block and its ack, the closes, the stop |
| `src/collector/HSMDataCollector.Tests/Tables/` | Unit tests (both TFMs: linked into the net8 IntegrationTests) |
| `src/collector/HSMDataCollector.Tests/Transport/TableRegistrationSessionTests.cs` | Registration over a real session |
| `src/collector/HSMDataCollector.Tests/Transport/TableConnectionTests.cs`, `ScriptedSessionServer.Tables.cs` | The table connection against the scripted server, and that server's table side |
| `src/collector/HSMDataCollector.IntegrationTests/Tests/TableSensorEndToEndTests.cs` | Rows through the real server and back through `tableHistory` |
| `tests/conformance/collector/table_sensor_contract.hsmtest`, `table_connection_contract.hsmtest` | The portable behaviour (rule 9) |

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
   each block is written with one `TableBlockWriter` (reused) and checked as written against the frame limit and every
   block limit the table connection checks before a put (decompressed bytes, rows, tables): a block that breaks one is
   split by tables, then by rows, and packed again; a single row that breaks one on its own (a server may state a
   decompressed limit smaller than a row) is dropped and counted as too large. Blocks get the next producer sequence
   and go to the end of the backlog; the buffer room goes back. A pack observes its token (the run's; at stop the drain budget) before every
   compression pass: the rows it has not packed when the token fires are dropped and counted. The tick then signals the
   delivery loop. Nothing is reported while a gate or lock of the pipeline is held: reports are queued and written once
   the gates are released, so a host logger that calls back into the collector (even `Dispose`) cannot deadlock it.
3. **Delivery** (`DeliverAsync`, a loop of its own): the head of the backlog is put on the table connection (below);
   the next block goes only after the answer to the one before. A block stays in flight, out of reach of the backlog
   bound, until its answer is applied: the removal from the backlog, the end of in-flight and the accounting are one
   step under the backlog's lock.
   An answer that came back is applied whatever the token says by then: a block the server stored is never counted as
   lost.
4. **Stop** (`FlushAsync`, inside `DataProcessor.StopWithFlushAsync`): runs on a thread of its own beside the value
   queues' drain under the same budget (one connection each, so the stop is not longer and packing never holds the value
   drain back). It takes the run out of the pipeline and begins its drain: from that moment the run's rows are refused
   and counted until the next `Start`, also while the collector is still `Stopping` for its value queues. It ends the
   tick and the loop, runs the final pack (after a pack of the run that is under way), delivers until the budget is
   spent or a block is not delivered, closes the table connection (before the session is closed; within the budget
   but for at most 1.25 s, see "Stop" below), then closes the run (nothing of it is packed or enqueued again) and
   drops and counts the rest (the backlog, and rows of sensors still `Pending`). A start rollback does the same
   without delivering. An answer to a registration request of an earlier run changes nothing: every run registers
   its sensors itself.

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

### The table connection

Blocks travel on a second WebSocket of the session (`Client/Session/TableConnection.cs`; the contract is "Table
connection" in `aicontext/features/api/wire-contract/feature.md`, the server's side
`aicontext/features/server/collector-session/feature.md`). The session sender owns it and makes it with the first
table sensor; a collector without table sensors has no object, no loop and no task of it.

- **When it is opened.** By a loop of its own, never by `AddRow` or the tick: once a table registration of the run
  was accepted, and only while the session is up. The upgrade is `GET` of the session route with
  `Sec-WebSocket-Protocol: hsm.tables.v1`, the headers the session sends (`Key`, `ClientName`, the collector kind and
  version, `ExtraRequestHeaders`) and `X-Hsm-Session`: the `sessionId` of the session's hello. There is one attempt at
  a time and none while the session is down; the pauses between attempts are the session's backoff with its jitter
  (`SessionTimings.MinBackoff` to `MaxBackoff`), starting over after a connection that lived a minute. A session
  whose hello states no `sessionId` (a server before table sensors) gets no attempt, which is said once.
- **Hello.** The server's first frame states `maxFrameBytes`, `heartbeatSeconds` and `blockLimits`. From then on the
  pipeline packs for those numbers (`ITableBlockChannel.MaxBlockBytes` is `maxFrameBytes` less the 40-byte frame
  header; `BlockLimits`); before the first hello it packs for the defaults (4 MiB, the default block limits). A hello
  the collector cannot work with (a frame limit of 104 bytes or less, a heartbeat outside 1 s to 1 h, block limits the
  format cannot take) closes the connection with `1002`.
- **Probe.** Right after the hello the collector sends a header without a block (kind 2) and takes the connection as
  up only when its ack, `200`, comes back as the first answer, within the open timeout (15 s).
- **One block at a time.** A block is one binary message: the header (frame version 1, kind 1, the request id, the
  producer id, the producer sequence) and the block, written as two fragments so the block is not copied. The
  collector then waits for the text ack of that request id up to `CollectorOptions.RequestTimeout` (30 s). **No ack in
  time closes the connection** (with a close frame, `1000`, not a reset), so a late ack cannot be taken for the next
  block's, and the same frame content (the same producer id and sequence) goes on the next connection; a block the
  server had stored is answered `duplicate` there. One put follows its block across up to three lost connections,
  waiting up to 15 s for each (`SessionTimings.TableConnectionWait`); after that it answers "not delivered" and the
  block goes again with the next delivery. Writing a frame is bounded by ten heartbeats (at least 10 s): a frame over
  64 KiB may wait unread on the server for its frame budget.
- **Heartbeat.** As on the session: a `ping` when nothing was sent for a heartbeat, and the connection is given up
  after two and a half heartbeats without a frame from the server, except while a block is being written.
- **What the collector takes from the server**: text frames only, and of those acks, `ping` and `pong` (a push with an
  `op` it does not know is ignored, as on the session). Everything else is a protocol error: a binary frame (the
  collector closes with `1003`), a text that is not a JSON object (`1007`), an ack of a request it never sent or a
  frame that is neither an ack nor a push (`1002`), a text frame larger than 1 MiB (`1009`; an ack lists at most 100
  refused tables, so a larger one is a broken server). The connection is closed with that code, the error is reported
  once, the connection is opened again after the backoff, and the block that waited is sent again.

**The ack of a block**, by status. A refusal a repeat cannot cure is never sent again; a fault is never final. The
server sends `200`, `400`, `503` and `500` for a block (`CollectorSessionHandler.HandleBlockAsync`); the other rows
say what the collector does should a later server send them.

| Ack | Answer to the pipeline | Can a repeat cure it? | The block |
|---|---|---|---|
| `200` (any 2xx), `stored` | `Stored` | settled | leaves the backlog; refused tables as under "Answers to a block" |
| `200`, `duplicate`, with `refused` or with `"refusedKnown":false` | `Duplicate` | settled | leaves the backlog; the refusals listed are applied |
| `200`, `allRefused` | `AllRefused` | no | dropped, every row counted, the reasons applied |
| `200`, an outcome this version does not know, or a body that is not JSON | `Stored`, reported once | settled: a `200` settles a block | leaves the backlog |
| `400`, and every 4xx not named below | `Invalid` | no: the same frame would be refused again | dropped, every row counted, reported |
| `408`, `429` | `NotDelivered`, with `retryAfter` | yes | kept at the head, same producer and sequence |
| `503`, `500`, every other 5xx | `NotDelivered`, with `retryAfter` (capped at 5 minutes) | yes | kept at the head, same producer and sequence |
| any other status (1xx, 3xx, below 100, 600 and above) | `NotDelivered`; the connection is closed with `1002` | yes | kept |
| no ack within `RequestTimeout` | none: the connection is closed | yes | the same frame on the next connection |

**A refused upgrade**, by status (the statuses are the server's, `CollectorSessionHandler.HandleAsync`). In every
case the blocks wait in the backlog under its bound (`MaxTableBacklogBytes`; the oldest are dropped and counted), and
the refusal is reported once until it changes.

| Status | What the server says | The collector |
|---|---|---|
| `404` | table connections are off (`CollectorSessions.MaxTableFrameBytes = 0`), or sessions are | asks again every 5 minutes (`SessionTimings.TableRefusedRetry`) and with the next session (a restarted server comes with one) |
| `400` | it does not know `hsm.tables.v1` (a server before table sensors) | asks again every 5 minutes and with the next session |
| `409` | `X-Hsm-Session` names no live session of this key | no further attempt with that session; the next session is the next chance |
| `406` | the key cannot send | tries again with the session's backoff |
| `503` | it is shutting down | tries again with the session's backoff |
| any other, no answer within the open timeout, a connection that fails | | tries again with the session's backoff |

**A close**, and the end of the session:

| What ended the connection | The collector |
|---|---|
| `1013` from the server: its wait for the table frame budget passed its bound, the block was not stored | opens a connection again after the session's backoff with jitter and sends the same frame; the block is never dropped for it |
| `1000` from the server: replaced by a newer table connection, or its session ended | waits for the end of its own session or one backoff, whichever is first, then connects under the session that is up |
| the collector's session ended, or a new session is up | closes the table connection and opens one that names the new `sessionId`; the block in flight is sent again on it |
| `1001` (shutdown), `1008` (the key can no longer send), `1002` / `1007` / `1009`, a connection lost without a close | opens a connection again after the backoff; the block is kept |

The server holds **one large-frame reservation per access key**, so collectors that share a key and send frames over
64 KiB are served one after another, and one that waits past the server's bound (180 s by default) is closed with
`1013`. The collector's answer is always the same frame again; collectors that see `1013` repeatedly need keys of
their own (the server's log names the key as the cause).

**A hello with smaller limits than a block was packed for** (a server reconfigured between two connections, or a
block packed before the first hello): the put answers `TooLarge` without sending, and the pipeline reads the rows
back from the block and packs them again into blocks that fit, in the block's place, each of at most half the
block's rows (whatever limits the pipeline reads at that moment: the put may have judged by a newer hello); a row that
breaks a limit on its own is dropped and counted as too large. A `TooLarge` for a block of one row is final: the row
is dropped and counted as too large, reported once. So every `TooLarge` removes the head or halves it: the answers
`TooLarge` to the rows of one block are at most twice its rows, and no answer keeps the delivery loop turning without
progress or a wait. The new blocks take its sequence, or the next one when the block may have reached the server: a
put of it ended without an answer, or this put wrote its frame on a connection that was lost before the ack (the put
says so with its `TooLarge`). The blocks behind it, which were never put, are numbered after them. So a sequence
that was on the wire is never given to other rows, and the next sequence never goes back below one.

**Stop.** The table drain ends by closing the table connection (`ITableBlockChannel.CloseAsync`, which is
`TableConnection.StopAsync` with a linger of one second), before the session is closed: a close frame `1000` (written
even when the budget is spent; it is bounded by 250 ms on its own), then the server's answer for at most a second or
what is left of the budget, then the socket is aborted and the connection's loop is waited for, up to a second. That
last wait is outside the drain budget: the loop ends as soon as its socket is aborted, so it is short in practice, and
it is kept so that the table connection is down before the session's close is sent. A stop can so pass its drain
budget by at most about 1.25 s (the close frame and the wait for the loop). A block in flight when the budget ends
stays in the backlog and the run's close counts its rows once, as not delivered at stop. The next `Start` starts the
loop again and opens nothing until a registration of the new run was accepted.

**Dispose**, and the rollback of a failed start, close the run without delivering (`TablePipeline.StopWithoutFlushAsync`,
also reached from `TablePipeline.Dispose` and from the terminal stop of `DataCollector.Dispose`): the run's tick and
delivery loop are ended (the wait for the delivery loop is at most a second), the table connection is closed through
the same `CloseAsync` with a budget already spent, so the close frame `1000` is still written (at most 250 ms) and no
answer is waited for, the socket is aborted and the connection's loop is waited for up to a second; then everything
the run holds is dropped and counted. So the table part of a dispose blocks the host for at most about 2.25 s (in
practice milliseconds: both loops end on their cancellation) and never throws, from any state.
`TableConnection.Dispose` itself (the session sender's dispose, after that) only cancels and aborts: no handshake, no
wait.

**.NET Framework.** The code is the same on both targets: `ClientWebSocket` takes the subprotocol, the request
headers (`Key`, `ClientName`, `X-Hsm-Session`, the collector headers) and fragmented binary messages on net472 as
on net6.0, and the table connection configures its socket through the session's own `SessionConnection.Configure`.
What differs: (1) net472 has no per-connection certificate callback, so with `AllowUntrustedServerCertificate` on
`wss` no session can exist there, and so no table connection: table sensors are `Unavailable`, as decided for the
session; (2) a refused upgrade says its status differently, in the `HttpWebResponse` of a `WebException` inside the
`WebSocketException` on .NET Framework and only in the exception's text on .NET 6 (`TableConnection.UpgradeStatus`
reads both; a status it cannot read is treated as "any other"). Checked on net48: the sockets of `ClientWebSocket`
do not count against `ServicePointManager.DefaultConnectionLimit` (five to one host with a limit of two), so the
second socket does not compete with the session or with the host's own requests.

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

This is what the pipeline does with the answer of its channel; which ack of the table connection gives which answer
is the table under "The table connection".

## Loss and its reports

Every lost row is counted in its sensor's `RowsDropped` and in the **loss ledger of its run** (one per run, under no
gate). At every tick and at the end of the table drain the ledger reports what it gathered: one deduplicated line per
reason through `Collector errors`, and the rows to the `Queue overflow` diagnostic sensor under the queue
**"Table rows"** while that sensor takes values (from its start until it has published its last bar of the run; the
sensor refuses them after that, atomically with its last bar). Rows the sensor did not take stay in the ledger. The
sensor's stop first lets a periodic send in flight end (the bounded wait of every sensor stop) and then publishes its
last bar: a loss the sensor took after that send's snapshot is in the last bar (`BarMonitoringSensorBase.StopCoreAsync`,
the same for every bar sensor).

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
| row too large / packing failed | a single row larger than a frame or a block limit of the table connection (at the pack, or a `TooLarge` for a block of one row); a fault of the packer (only the rows in no block queued before it, once) |
| buffer could not grow | an allocation failed while a sensor's buffer grew (out of memory) |

## Storage / Persistence

None yet: rows and blocks live in memory only, and a new producer id is taken at every `Start`. The opt-in local
file of undelivered blocks with its own bound comes with part 3.

## UI / Operator Visibility

`Collector errors` and the collector log (the lines above), `Queue overflow` with queue "Table rows", and on the
sensor itself `State`, `StateReason`, `RowsAccepted`, `RowsDropped`, which an application reads without the server.

## Dependencies

- Depends on: `src/api/HSMTableBlocks` (block writer, schema id), `src/api/HSMSensorDataObjects` (registration DTO),
  the collector session and its table connection (`aicontext/features/api/wire-contract/feature.md`, "Table sensor
  registration" and "Table connection"; the server's side in `aicontext/features/server/collector-session/feature.md`),
  `CollectorScheduler`, `DataProcessor` (data gate, error channel, stop drain).
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
  the rows of an `Unavailable` sensor in the overflow sensor or the stop line, a table sensor created during a start
  rollback, and a stress test of Start/Stop/Start/Stop/Start/Stop: the writers start after the first Start and write
  through every boundary (each stop is held by hooks until every writer has written more), every run accepts a minimum
  from every writer, and every delivered row is checked against the run that accepted it (recorded at `AddRow`).
  `QueueOverflowFinalBarTests`: a loss the overflow sensor took after the snapshot of a periodic send in flight is in
  the bar its stop publishes.
- The table connection, both TFMs, against `ScriptedSessionServer` (which takes the table upgrade, sends a hello with
  settable limits, decodes each binary frame with the block reader and answers it as scripted): `TableConnectionTests`:
  the upgrade, hello and probe; every ack status; a lost ack and its `duplicate` (the end of the wait is decided by the
  test through `AckDeadlineForTests`, not by a clock); `1013`; `1000`; each upgrade refusal (404, 400, 409, 406, 503)
  and the 404 / 400 asked again with the next session; a block in flight across a new session; splitting by the
  hello's frame limit and block limits, a block packed before a smaller hello, and one packed again after a lost ack
  (new sequences, never the stored block's); a stop with a block in flight; a binary frame, an oversized frame, a text
  that is not JSON and a foreign ack from the server; the heartbeat; start, stop, start; dispose and stop from every
  state; no table connection object without a table sensor.
- End to end, net8.0, the Docker-backed lane: `TableSensorEndToEndTests` registers a table sensor on the real server,
  delivers rows at a tick and in the stop drain, and reads them back through `POST api/sensors/tableHistory`.
- Conformance: `tests/conformance/collector/table_sensor_contract.hsmtest` through the .NET driver and, since 0.15.0,
  the native driver (tables build; see "Native collector"); `table_connection_contract.hsmtest` through both drivers
  on their scripted session servers (the native driver since 0.15.1, in builds with the session transport and table
  sensors).

## Known Issues / Limitations

- No local file: a crash or a stop that cannot deliver loses what is held (counted); a restart takes a new producer id.
- **Rows can be stored twice after a smaller hello.** A block whose frame went out without an answer (a lost ack) may
  be on the server. When a connection's hello then allows less than that block needs, its rows are packed again under
  new sequences after its own, and a server that did store the block stores those rows a second time: it cannot
  recognise them as a duplicate. A row that the new limits do not take at all is dropped and counted as too large,
  although the server may hold it in the first block (counted as lost, perhaps stored). It takes a lost ack and a
  server whose limits were lowered between two connections. What cannot happen: rows lost without a count because a
  new block took the sequence of a block the server held and was answered `duplicate`.
- **A `409` is not asked again within its session.** The collector waits for its next session; when the server
  refused a session that is in fact alive, blocks wait until that session ends.
- **Collectors that share an access key take turns with frames over 64 KiB** (one reservation per key on the server):
  see `1013` above. The collector cannot tell that cause from a spent budget; the server's log does.
- **A block waits for the table connection for at most 15 s per put** and three connections; while the connection
  cannot be opened, each delivery reports "not delivered" (deduplicated) and the backlog holds the blocks under its
  bound.
- `MaxTableBufferBytes` bounds the rows waiting for the tick (8 bytes per column per row). The arrays behind them are
  bounded apart from it: a buffer that grew past 256 rows gives its arrays up when a tick has packed it, so a quiet
  sensor keeps at most two buffers of 256 rows (4 KiB per column); the arrays of a busy sensor follow its rows. A busy
  sensor therefore allocates its arrays again every tick.
- Rows of refused tables beyond the first 100 of one acknowledgement are not in `RowsDropped`: the server lists at
  most 100 refusals and gives only the number of the rest (`refusedCount`), so the collector knows neither their tables
  nor their rows. The collector logs both numbers.
- **A logger that disposes the collector from the stop line deadlocks.** The one line `Collector stop: N table row(s)
  ...` is written by the stop itself (`TablePipeline.ReportStopLine`); a host `ICollectorLogger` that calls
  `Dispose()` synchronously inside the callback of that line waits for the stop task that is waiting for the logger
  (`DataCollector.Dispose` joins the stop in flight). Every other report of the tables is written outside the gates and
  may dispose.
- **A count can miss the reports when a thread is paused across a whole stop.** A writer refused by a full buffer
  counts the rows in `RowsDropped` first and in the ledger second (`TableSensor.Drop`); paused between the two until
  the stop line has closed the ledger, its rows stay on the sensor and are in no report. The same for a tick that took
  the unreported count from the ledger (`TableLossLedger.TakeUnreported`) and outlasts the stop's wait for it (1 s):
  the overflow sensor no longer takes the count and the closed ledger does not take it back.
- **A block stored too late is counted as lost.** When the `stored` answer of a block arrives after the delivery wait
  and the drain budget of the stop have expired, the stop has counted its rows as not delivered although the server
  has them. The count errs on the side of loss.
- **A registration refusal that lands between two runs stays.** A refusal answered for run R1 and applied after R1
  closed, before the next `Start` attached the sensor to R2, still finds the sensor in R1 and makes it `Refused`
  (`TableSensor.TryMove`); `Start` puts only `Registered` sensors back to `Pending`, so R2 does not register it. The
  outcome is that of the same refusal a moment earlier: a refusal is final.
- **A last bar that cannot be published.** A periodic send of `Queue overflow` that outlives the bounded wait of the
  sensor's stop (1 s; the send only enqueues a value) still owns the send guard when the last bar is due: that bar is
  not published, and the losses the sensor took after that send's snapshot are in no report.

## Native collector

The native C++ collector (`src/native/collector`) has table sensors since 0.15.0 (#1848 PR 3). The behaviour is the
one above; this section says how it is reached and what differs.

**API.** The C ABI in `hsm_collector.h` (`hsm_collector_create_table_sensor`, `hsm_table_sensor_add_row` and
`_add_rows` (all or nothing), `_get_stats`, `_state_reason`, `_path`, `_release`, `hsm_collector_set_table_options`;
`docs/native-collector-c-abi.md`, "Table sensors") and the C++ wrapper `include/hsm_collector/tables.hpp`
(`Collector::CreateTableSensor(path, TableSensorOptions)`, move-only `TableSensor` with `AddRow(time_point,
{cells})`, `AddRows`, `Stats()`, `Path()`; `TableCell` is a double, an int64 or `TableCell::Null()`). The time is
Unix nanoseconds, kept to 100 ns (rounded down). Settings: `hsm_table_options_t` (`tick_period_ms` 10 s, at least
1 s; `max_buffer_bytes` and `max_backlog_bytes` 64 MiB), set before the first table sensor.

**Build option.** The ABI exists in every build. Packing needs `HSM_COLLECTOR_TABLES` (zstd, zlib; vcpkg feature
`tables`); without it every table sensor is `Unavailable` ("built with HSM_COLLECTOR_TABLES=OFF"). Delivery needs the
session transport too, so `HSM_COLLECTOR_HTTP` as well. The agent and the probe build with tables OFF.

**What works since 0.15.1 (#1848 part 4).** A collector on the session (`hsm_collector_use_session_transport`) built
with both options delivers its table sensors: the registrations as a `commands` request of their own on the session
(the wire of the managed golden `Registration_of_a_table_sensor_carries_its_columns_after_previous_paths`), the
blocks on the session's table connection (`SessionTableChannel`, `src/tables/hsm_session_table_channel.{hpp,cpp}`,
over `AckedBinaryLink`, `src/hsm_acked_binary_link.{hpp,cpp}`). The table connection is the managed one: opened
only while the session is up and once a registration of the run was accepted, named after the session by
`X-Hsm-Session`, the hello's `maxFrameBytes` and `blockLimits` the limits the pipeline packs for (`MaxBlockBytes`,
`Limits`; the defaults before the first hello), the probe and its `200` before the connection counts as up, one block
in flight, the ack table above (2xx `stored` / `duplicate` / `allRefused`, an unknown outcome or a body that is not
JSON taken as stored and said; `400` and every other 4xx `Invalid`; `408`, `429` and every 5xx not delivered with
`retryAfter`, capped at 5 minutes; any other status closes with `1002`), no ack within the request timeout closes the
connection with `1000` and the same frame (same producer and sequence) goes on the next connection, up to three
connections per put, each waited for up to 15 s; the refused upgrades (`404` and `400` said once, asked again every 5
minutes and with the next session; `409` waits for the next session; `406`, `503` and the others with the session's
backoff); the closes (`1013` and every other close or a lost connection: reconnect with the backoff and send the same
frame; `1000` from the server: a new session or a backoff, whichever is first; a new session replaces the connection
at once); what the server must not send (a binary frame `1003`, a text that is not a JSON object `1007`, a frame that
is neither an ack nor a push or a foreign ack `1002`, a text over 1 MiB `1009`: closed, reported once, reconnected,
the block sent again); the heartbeat (a `ping` when nothing was sent for the hello's heartbeat). A collector on HTTP,
or built without either option, or whose libcurl cannot carry a WebSocket, keeps its table sensors `Unavailable` with
that reason. A collector without table sensors makes nothing of it: no channel, no connection, no thread, no line
(`native_table_no_connection_without_a_table_sensor`).

**Threads of the table connection.** It has none of its own. The pipeline's delivery thread owns it (in tests with a
manual tick, the caller of the tick): it opens the connection when a registration is accepted, sends the blocks and
reads their acks, and between ticks wakes when the channel asks for it (`ITableChannel::ServiceDelayMs` and
`Service`, the seam's two additions of 0.15.1): to open the connection again after a pause, to send the heartbeat, and
once a second while the connection is open (to read what the server sent meanwhile and to notice a new session).
`Cancel` (the stop's budget, a dispose) reaches the connection from any thread. The channel writes its lines only
through `ITableChannel::WriteReports`, which the pipeline calls with no lock held (its `Register` and `Put` run under
the pipeline's locks, where nothing is logged).

**Stop.** The table drain delivers within the stop budget as before; then, after the delivery thread is joined and
before the session is closed, the table connection is closed with `1000`, waiting for the server's answer for what is
left of the budget and at most a second (with the budget spent, the close frame is written once and nothing is
waited for). A block in flight when the budget ends is cancelled: its connection gets a close frame `1000` (one
non-blocking write) and its rows are counted once as not delivered at stop
(`native_table_stop_with_a_block_in_flight`). Dispose and destroy do the same without the drain.

**Threads and lifetime.** The first table sensor makes the pipeline; a collector without one has no pipeline, no zstd
context, no thread, no tick and no log line of this feature. From Start to Stop the pipeline has a tick thread (a
second `ScheduledTask` on the collector's clock) and, when a channel exists, a delivery thread (registrations, then
the backlog, stop and wait). Stop, on the caller's thread: rows are refused from now on; the tick is stopped and
joined; one stop budget starts here and covers the final pack and the delivery, as the managed drain budget; every
sensor's mutex is taken once for the final take, and the rows are packed (Registered sensors) or dropped (the
others), the budget observed before every compression pass, so what is not packed by its deadline is dropped and
counted as not delivered at stop in one report (`native_table_final_pack_observes_the_stop_budget`); the losses so far
go to the `Queue overflow` sensor before its last bar; the delivery thread drains the backlog within what is left of
the budget beside the value drain, is cancelled if the budget runs out and is joined;
what is left is dropped and counted; the session stops as before; the one stop line names the rows the overflow sensor
did not get. **When Stop returns no thread of the run exists**, so no tick, registration answer or acknowledgement of
one run can reach the next: the managed run object, its generation checks and the races its three review rounds fixed
(Start/Stop/Start, accounting at stop, a host without table sensors paying nothing) have no counterpart to get wrong.
A writer preempted between its reservation and its append across a whole Stop and Start appends into the run that is
accepting when it takes the sensor's mutex; the buffer counter is never reset, every reservation is given back by its
owner. A sensor handle holds the sensor, and the sensor only a shared block of flags, the bound and the ledger: never
the collector, so an application thread can never become the collector's last owner; after the collector is disposed
its calls return `HSM_RESULT_INVALID_STATE`.

**Failures.** A pack that fails at any step (an allocation of its pieces, groups, a split, a block, the backlog or a
report) settles every row it took on every exit, as the managed catch and finally do: a row is in a queued block or
counted once as "packing failed" (on its sensor and in the ledger), and its room in the buffer bound goes back, so a
failed pack never shrinks the bound for later adds. The tick's pack and the stop's final pack alike. No exception of
the tables leaves a C function or a thread: a start whose threads cannot be made (the host's thread limit) is rolled
back, as the managed `Start` that never throws: no thread of the table run is left, rows are refused and counted, the
sensors are `Unavailable` for the run with that reason, and `hsm_collector_start` returns `HSM_RESULT_OK` for the rest
of the collector (`native_table_start_failure_rolls_back`); the next Start tries again. A stop, a dispose and a destroy
always complete: the delivery thread is joined and what was not delivered is counted, whatever step of the drain fails
(`native_table_stop_failure_completes`). The tick's and the delivery thread's failure handlers allocate nothing: they
raise a flag that the next report writes as a fixed line.

**Reports and locks.** Every report of the tables reaches the host's logger with no lock of the pipeline and no lock
of the collector's table state held, so a logger may call back into the collector on any path (create a table sensor,
read its stats: `native_table_reports_reach_the_host_with_no_lock_held`). The one lock held is the collector's
lifecycle lock: what Start, Stop and Dispose report (the run's start, the losses at stop, the stop line) is written
while it is held, as every lifecycle notification and log line of the native collector is, so a logger must not call
`hsm_collector_start`, `_stop`, `_dispose` or `_destroy` from those lines (the managed known issue of the stop line,
for every lifecycle report).

**Differences from the managed collector.**

- The table connection has no loop of its own: the delivery thread keeps it (see "Threads of the table connection").
  It opens the connection as soon as a registration is accepted, inside that registration; a put that finds none opens
  one itself (one attempt at a time either way, the same backoff). While a block's put waits for its ack, nothing
  else of the table connection runs: the registrations of the run wait for the put, as they wait for the managed
  delivery loop.
- No silence limit. The managed connection is given up after two and a half heartbeats without a frame from the
  server; the native one is read only when the delivery thread is there, so a dead connection is found by a failed
  write, a ping that cannot be written, or the ack timeout of the next block (then the same frame goes on the next
  connection, as for every lost ack). The heartbeat is sent from the wake the channel asks for, at the heartbeat's due
  time.
- A block is one binary message from one buffer: the pipeline reserves the 40 header bytes in front of the block, the
  channel writes the producer and the sequence there and the link the version, kind, length and request id. The
  managed collector writes the header and the block as two fragments of one message.
- A refused message of the server (binary, over 1 MiB) ends in an orderly close: the rest of it is read and dropped
  until the server's close, at most a second, then the connection is released. Managed closes with the code and
  aborts.
- The stop closes the table connection with a linger of what is left of the stop budget, at most a second, after the
  drain; managed writes the close frame within 250 ms, waits up to a second for the answer and up to a second for its
  loop, so it can pass its budget by about 1.25 s. The native close stays in the budget, except for the open item
  below.
- An empty description is registered as `null` (the C ABI has no unset description apart from an empty one); managed
  sends `""` for an empty string and `null` for none.
- `Unavailable` is decided at creation and again **at every Start** (the managed collector decides it once, when it is
  constructed): native transports are chosen by a call before Start. A sensor `Unavailable` in one run is `Pending` in
  the next if a channel exists by then. `Refused` stays final in both.
- A table the channel's limits can never take (a path longer than its `max_table_id_length`, more columns than its
  `max_columns_per_table`; the create checks the default limits only) is `Unavailable` with that reason as soon as
  the limits are known: at Start, or at its creation when a channel exists. Its rows are refused and counted as
  `SensorUnavailable`, it is never registered, and the other sensors are not touched. Should the limits change inside
  a run, a pack does not split such a table down to single rows: its rows of the pack are one "packing failed" loss,
  reported once per sensor (`native_table_over_connection_limits`). The managed collector registers such a table, and
  each of its packs throws on it: every row that pack took, of every sensor, is counted as "packing failed" and the
  failure is reported at every tick. Limits a split can satisfy (tables, rows, decompressed bytes, the frame) split as
  in managed.
- A cell is a raw 8-byte union read by the column's type, so a double given to an int64 column is not detected (the
  managed `TableCell` knows its kind and throws). The C++ wrapper's `TableCell` does detect it and throws.
- A create on a disposed collector is refused with `HSM_RESULT_INVALID_STATE`, like every native create (managed gives
  an `Unavailable` sensor). A create that races the dispose is ordered with it under the lock of the table state: it
  is either before the dispose (its sensor is disposed with the rest) or refused, with nothing made
  (`native_table_create_racing_dispose_is_refused`).
- A `TooLarge` answer (a frame limit that shrank after packing; only a real connection gives it) is final: the
  block's rows are dropped and counted once as "row too large", and the block is never packed or sent again (the
  pipeline keeps no rows after packing). Its sequence was on the wire, so it is never given to other rows: sequences
  only rise. A row that alone breaks a limit (the frame, or the decompressed bytes) is dropped once at packing and never
  put. So no input makes delivery loop without progress (a block settled or dropped) or a wait for the next tick
  (`native_table_too_large_is_final_and_sequences_never_go_back`). The rows a pack drops as too large are reported
  in one line per pack (managed writes a line per row and its error deduplication collapses them).
- The loss report goes to the one `Queue overflow` bar (native has no per-queue split); the stop line is the managed
  one.
- A writer paused between the moment it reads that the ledger is open and its count, across a whole stop, has its rows
  in the next run's first report instead of none (the managed known limit loses them from the reports).

**Known limits of the table connection (0.15.1).**

- **A stop can wait for a block write that does not end.** On Windows with TLS (Schannel) a libcurl WebSocket send can
  block past its timeout when the server stops reading in the middle of a large frame (measured: a 16 MiB send with a
  2 s timeout returned after 20 s). The stop's cancel cannot interrupt it there, and the stop joins the delivery thread
  before it closes the table connection and the session, so a stop (and a dispose) can last as long as that write: the
  frame's size over what the server still reads, bounded by the write's own bound of ten heartbeats (at least 10 s)
  only where libcurl honours it. How to bound it is open (branch `feature/1848-native-send-bound`). Where libcurl hands
  the write back between its pieces, a cancel is seen within one wait slice (100 ms).
- No run against a real HSM server is part of 0.15.1: the table connection is proven against the scripted table side
  of `tests/ws_test_server.hpp` (a second, independent implementation of the server's side) and the shared fixture.
- The heartbeat waits for the delivery thread: while a registration or a put is under way (each bounded by the
  request timeout) no ping is sent; a block's ack wait sends one when a heartbeat passes without a write.

**Tests.** `src/native/collector/tests/hsm_table_pipeline_tests.cpp`: the parts (buffer growth, failed growth, null map
on first use, swap; the bound's compare-and-swap under eight threads; the backlog with a block in flight; splitting;
paths; the ledger), the ABI and the wrapper, a host without table sensors, a handle that outlives its collector, the
threads that exist only while running, a writer held across Stop and Start, a bounded drain, losses at stop, a pack
failing at each of its allocations (`native_table_pack_failure_settles_every_row`), and a
stress test (four writers, three sensors, eight runs of three ticks, a stop and a start, each boundary met by writes in
flight; per sensor offered = accepted + refused, dropped = refused + lost after acceptance, every delivered row
accepted once and under a run that was accepting during its call). The shared fixture
`tests/conformance/collector/table_sensor_contract.hsmtest` and its must-fail fixture run in the native tables build.

The table connection (0.15.1; builds with HTTP and tables, against the scripted table side of
`tests/ws_test_server.hpp`; no case is decided by a duration: the end of an ack wait is the script's, through
`SessionTableChannelOptions::ack_abandoned_for_tests`): `native_table_block_after_hello_and_probe` (the upgrade's
headers and `X-Hsm-Session`, the probe first, one frame, the registration byte-identical to the managed golden, with a
description and a retention too), `_every_ack_status_maps_to_its_answer` (eleven acks: stored with `UnknownSensor`,
duplicate with refusals and with `refusedKnown: false`, `allRefused`, an unknown outcome, 400, 404, 408, 429, 500,
503), `_lost_ack_then_duplicate`, `_close_1013_sends_the_same_frame`, `_close_1000_sends_the_same_frame`,
`_upgrade_404_asked_again_later`, `_upgrade_400_asked_again_later`, `_upgrade_404_asked_with_the_next_session`,
`_upgrade_400_asked_with_the_next_session`, `_upgrade_406_uses_the_backoff`, `_upgrade_503_uses_the_backoff`,
`_upgrade_409_waits_for_the_next_session`, `_block_in_flight_across_a_new_session`, `_splits_by_the_hello_limits`
(frame limit and `maxRows`), `_block_packed_before_a_smaller_hello` (final, rows counted once, the sequence not given
to other rows), `_stop_with_a_block_in_flight`, `_server_protocol_errors` (binary 1003, over 1 MiB 1009, not JSON 1007,
a foreign ack and a frame neither ack nor push 1002, a status 302 1002; each said once, the block stored on the next
connection), `_heartbeat`, `_start_stop_start` (a connection and a producer per run), `_dispose_from_every_state`
(created, no session, registered, up, a block in flight, refused upgrades, stopped; dispose and destroy),
`_no_connection_without_a_table_sensor`, `_link_carries_4_and_16_mib_frames` (the link itself), and
`_stress_writers_reconnects_and_stop` (four writers against twelve ticks, a 1013 and a dropped session every four,
then Stop: every accepted row stored once or counted as lost, a sequence on the wire always with the same bytes).
`table_connection_contract.hsmtest` runs through the native driver in the same builds.

## Open Points

- Wire contract against the server, found while writing the table connection (the collector follows the server):
  the contract says the key is checked first (`406` "whatever else is true") and lists `404` before `400`; the
  server answers `404` for sessions switched off before it looks at the key or at the upgrade, and checks the
  subprotocol (`400`) before table connections switched off (`404`). A collector with a valid key that offers
  `hsm.tables.v1` sees no difference; a collector with a bad key against a server without sessions gets `404`, not
  `406`. And a session that ends between the server's check of `X-Hsm-Session` and the attachment is not a `409`:
  the upgrade is accepted and closed with `1000` "its session ended" before any hello (the collector reports it and
  tries again). Either the contract or the server should be made to agree.

- Wire contract: an acknowledgement could carry the number of refused rows (or the indexes of all refused tables), so
  that a collector can count every lost row on its sensor also beyond the first 100 refused tables. A server change.
