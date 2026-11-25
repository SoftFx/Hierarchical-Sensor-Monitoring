# Feature: Table sensors in the web UI (chart and download)

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: what a user sees of a table sensor's rows in the web UI (#1847, step 5 of epic #1842, ADR-0021 point 14): the chart reduction on the server, and (later parts of the step) the endpoints of the page, the page itself and the download. The sensor kind is `aicontext/features/server/table-sensors/feature.md`; the read of rows is `aicontext/features/storage/table-blocks/feature.md` and `aicontext/features/storage/table-batches/feature.md`, section "The read".

---

## Overview

A table sensor can hold tens of thousands of rows an hour and years of them. A chart of a range does not send those rows to the browser: the server reads the range and reduces it to a minimum and a maximum per bucket of time and column, a few thousand numbers whatever the range holds. A short range (at most twice as many rows as buckets) is answered with its exact rows as well, so a zoom into a few seconds draws the real line.

Part 1 (this part) is the reduction in Core. The web endpoints, the page and the download follow in parts 2 and 3.

## The chart reduction (`ITableChartReader.Reduce`)

```csharp
TaskResult<TableChart> Reduce(Guid sensorId, DateTime from, DateTime to, IReadOnlyList<int> columns, int buckets, CancellationToken token = default)
```

Implemented by `TableBlockService` (`src/server/HSMServer.Core/TableSensors/TableBlockService.cs`), registered as `ITableChartReader`. It is a second interface beside `ITableBlockService`, not a member of it, so the test doubles of `ITableBlockService` stay as they are.

- **Range and times**: `[from, to)` in UTC, as `Read` takes them (an `Unspecified` time is UTC, a `Local` one is converted).
- **Buckets**: at most `buckets` (1..`TableChartReducer.MaxBuckets` = 10,000), of equal width `BucketTicks` = the range divided by `buckets` rounded up to a whole tick. Bucket `i` is `[from + i·BucketTicks, from + (i+1)·BucketTicks)`; the last one is cut at `to`, and there are `ceil(range / BucketTicks)` of them (fewer than asked when the range has fewer ticks). The bucket of a row is `(time - from) / BucketTicks` in integer ticks: no product of two times is formed, so the widest `DateTime` range does not overflow, and the last tick of the range lands in the last bucket.
- **Columns**: ordinals of value columns of the sensor's schema (1..count-1; the time is 0 and is not one), each at most once, in the order asked; an empty list answers the row counts only. Per bucket and column: `ValueCount`, the minimum and the maximum (`Int64Min`/`Int64Max` for an int64 column, exact over the whole `long` range; `DoubleMin`/`DoubleMax` for a double column), `NullCount`, `NonFiniteCount`; per column the totals `TotalNulls` and `TotalNonFinite`. A bucket has a minimum and a maximum only when its `ValueCount` is above 0.
- **Cells**: a null cell is counted, never compared, never read as 0. A double that is NaN or infinite is counted in `NonFiniteCount` and not compared, so one infinity cannot flatten the axis. A bucket with rows whose cells are all null has no value (a gap), and its `BucketRows` still counts them.
- **Rows mode**: while the range has shown at most `2 · BucketCount` rows, the rows are also collected exactly as `Read` collects them (`TableRowCollector`: sorted by time, equal times in the order they arrived, nulls kept); `TableChart.Rows` carries them when the read was complete. Past that the reducer drops them, and `Rows` is null.
- **Order does not matter**: a minimum and a maximum do not depend on the order of rows, so the reducer sorts nothing and keeps no rows beyond the rows mode. Rows out of time order, equal times and late blocks are the same to it. Its memory is a few arrays of the bucket count.
- **The scan limit** (`TableSensors.MaxChartScanRows`, default 10,000,000): the read visits its sources (the blocks of an arrival hour not merged, or one merged hour) in the order of their earliest row in the range, as `Read` does, and stops before the next source once it has gone through that many rows of the range. Every row earlier than that source's earliest row has then been seen. The bucket holding that time and every later bucket are emptied, and `CompleteToTicks` is the start of the first emptied bucket: every bucket before it is complete. Null when the whole range was read. The check is between sources, so one hour can take the count past the limit.
- **Incomplete answers**: `BlocksFailed` (stored blocks of hours not merged that could not be read) and `BatchesFailed` (batches of merged hours that could not be read) are counted as in `Read`; `IsIncomplete` is true when either is above 0 or `CompleteToTicks` is set. The rows of `Rows` are the same rows; the counters of `TableChart` are the ones to show.
- **Errors**: an unknown id, a sensor that is not a table sensor, `from` not before `to`, a bucket count out of range, a column that is not a value column or is named twice: an error result (the request is wrong). A store that cannot be used throws `TableBlockStoreException` (the caller answers 5xx). A cancelled token throws `OperationCanceledException`, checked before every block and every merged hour.

### The hour gate: held per source, not for the range

`TableHourGate` keeps the deletion of a merged hour's blocks out of a read in progress. It is a `ReaderWriterLockSlim`, which queues new readers behind a waiting writer: a read that held it for seconds would stall every other table read as soon as the merge's deletion asked for it.

So the chart holds the gate per source. Its plan (which hours from blocks, which from batches) is taken outside the gate. Under the gate, each source of blocks is decided again: when its hour has been committed by the merge since the plan, the hour is read from its batches instead (test seam `TableBlockService.BeforeMergedSwitch`). This keeps "an hour is read from exactly one source":

- an hour still not committed keeps its blocks while the gate is held, since its blocks are deleted only after its commit, and only under the write side of the gate;
- an hour committed has every row of the sensor's blocks of that hour in its batches (the merge's verification), and its commit is never taken back except by the retention of a whole week;
- a source decided as batches in the plan is unaffected by deletions.

A minimum and a maximum do not depend on which of the two sources the rows came from. Pinned by `TableChartReadTests.An_hour_committed_and_deleted_between_the_plan_and_its_read_is_read_from_its_batches`: the merge commits hour 0 and deletes its blocks between the plan and the first source; the chart still matches the reference and reads hour 0 from its batches. Without the re-check the test fails.

`Read` is unchanged: it still holds the gate for its whole range. Its result is a sorted page whose order and cut at a time boundary depend on every source of the range; holding the gate per hour for it was not proven equivalent within this part, and its length is bounded by `MaxReadRows` (measured below: about 170 ms for the default 1,000,000 rows from merged hours). See Known Issues.

### The read walker

`TableBlockService` splits the read into a plan (`Plan`: the candidate blocks per arrival hour, whether each hour is committed, the merged hours of the sensor's period class, sorted by their earliest row) and the visit of one source (`ReadSource`), which hands rows to an `ITableRowSink`: `TableRowCollector` for `Read`, `TableChartReducer` for `Reduce`. Taking one table out of a stored block (with its failure counting and logging) is `TableRowCollector.ReadBlockTable`, shared by both, so a block is decompressed once even when the chart also collects its rows. `Read`'s results are byte for byte what they were: the tests of `TableBlockServiceTests`, `TableMergeJobTests` and `TableReadRouteTests` are unchanged.

### Concurrency: `TableReadGate`

The number of table reads running at once (`TableSensors.MaxConcurrentReads`, default 2) is a Core singleton, `TableReadGate`, shared by the read route of the Sensor API (`TableReadService`) and the table pages of the web UI (part 2). A read beyond them waits for a turn. The route's answers are unchanged (it has no 429: it waits). A host that registers no gate (the Sensor API test host) gets a gate of the route's own.

## Configuration

| Key (`TableSensors`) | Default | Meaning |
|---|---|---|
| `MaxChartScanRows` | 10,000,000 | The rows of its range a chart reads before it stops before the next hour and answers the part it read in full. Validated 1..1,000,000,000,000 with an error that names the key. It bounds the time of a chart request; its memory does not grow with it. |
| `MaxConcurrentReads` | 2 | Shared by the read route and the web UI (`TableReadGate`). |

## Measured (2026-11-25, `TableChartBenchmark`, Release, Windows, 24 logical CPUs, machine shared with other work)

Synthetic random-walk data (time, bid, ask, no nulls), two runs each; the first call is a warm-up.

| Chart | Rows | Time | Allocated |
|---|---|---|---|
| 100 merged hours of one table, 1,000 buckets, bid and ask (2,500 batches) | 10,000,000 | 201–206 ms | 710 MB |
| One hour not merged: 360 blocks of 1,000 tables, 167 rows of the table and 7 of every other table in each, 1,000 buckets | 60,120 | 53–55 ms | 28 MB |
| For comparison, `Read` of the same merged range (the default limit) | 1,000,000 | 168–175 ms | |
| For comparison, `Read` of the same unmerged hour | 60,120 | 58–62 ms | |

- The allocation is transient: the decoded batches and decompressed blocks, about 70 bytes per row read. The reducer itself keeps a few arrays of the bucket count.
- With the gate held per source, the longest hold of a chart is one source: about 55 ms for an unmerged hour of this shape, about 2 ms for a merged hour of 100,000 rows.
- The synthetic times are evenly spaced and compress better than real ticks; on the real hour of the initiative a merged hour of one table read in 1.4 ms for about 2,200 rows. Not measured on Linux.

`HSM_TABLE_STORE_BENCH=1 dotnet test -c Release --filter "FullyQualifiedName~TableChartBenchmark" --logger "console;verbosity=detailed"`.

## Key Files

| File | Purpose |
|---|---|
| `src/server/HSMServer.Core/TableSensors/TableChart.cs` | `ITableChartReader`, `TableChart`, `TableChartColumn` |
| `src/server/HSMServer.Core/TableSensors/TableChartReducer.cs` | The reduction: buckets, cells, rows mode, scan limit |
| `src/server/HSMServer.Core/TableSensors/ITableRowSink.cs` | What a read hands rows to |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs` | `Reduce`, the plan and the visit of a source, the per-source gate hold |
| `src/server/HSMServer.Core/TableSensors/TableReadGate.cs` | The count of reads running at once |

## Tests

- `HSMServer.Core.Tests/TableSensors/TableChartReducerTests.cs`: the reduction against a brute-force reference (rows out of order, equal times, nulls, NaN, infinities, int64 and double, several batch and bucket counts); an empty range; the bucket edges (first tick, last tick, the end outside, a one-tick range, a range not divisible by the bucket count, more buckets than ticks, the widest `DateTime` range); nulls, NaN and infinities counted; int64 beyond 2^53 exact; the columns asked for in their order; rows mode and its end; the scan limit (complete up to a bucket boundary, everything after it empty); a damaged batch; the configuration's default and its validation.
- `HSMServer.Core.Tests/TableSensors/TableChartReadTests.cs`: a range over hours not merged and merged, the same before and after the merge; an hour committed and deleted between the plan and its read; a short range carries the rows exactly as `Read` returns them; the scan limit across arrival hours; cancellation between blocks; the refusals of a request.
- `HSMServer.Core.Tests/TableSensors/TableChartBenchmark.cs`: the measurement above (skipped unless `HSM_TABLE_STORE_BENCH=1`).

## Known Issues / Limitations

- `Read` holds the hour gate for its whole range (about 170 ms for 1,000,000 merged rows; about 60 ms per unmerged hour of the measured shape, so a read of a range with several unmerged hours holds it for that many times 60 ms). While it does, a deletion of merged blocks waits, and every read that starts after the deletion asked for the gate waits behind it.
- Rows of a block the merge could not read are not in its hour's batches. A chart that switches an hour from blocks to batches does not count them in `BlocksFailed`.
- The scan limit is checked between sources: one source can take the count past it.
