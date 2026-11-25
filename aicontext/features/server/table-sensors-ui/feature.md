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
- **Rows mode**: while the range has shown at most `2 · BucketCount` rows, the rows are also collected exactly as `Read` collects them (`TableRowCollector`: sorted by time, equal times in the order they arrived, nulls kept); `TableChart.Rows` carries them when the answer is complete (no failed block or batch, no stop at the scan limit). Past that the reducer drops them before it would copy or sort the rows of the source that passed the limit, and `Rows` is null.
- **Order does not matter**: a minimum and a maximum do not depend on the order of rows, so the reducer sorts nothing and keeps no rows beyond the rows mode. Rows out of time order, equal times and late blocks are the same to it. Its memory is a few arrays of the bucket count.
- **The scan limit** (`TableSensors.MaxChartScanRows`, default 10,000,000): the read visits its sources (the blocks of an arrival hour not merged, or one merged hour) in the order of their earliest row in the range, as `Read` does, and stops before the next source once it has gone through that many rows of the range. Every row earlier than that source's earliest row has then been seen. The bucket holding that time and every later bucket are emptied, and `CompleteToTicks` is the start of the first emptied bucket: every bucket before it is complete. Null when the whole range was read. The check is between sources, so one hour can take the count past the limit.
- **First and last row**: `FirstRowTicks` and `LastRowTicks`, the times of the earliest and the latest row the answer counts (in `RowCount`), null when it counts none; in both modes, independent of the order of the rows (kept per bucket as a minimum and a maximum of row times, dropped with the reducer when the chart plans again). After a stop at the scan limit they describe the rows counted, those before `CompleteToTicks`, not the whole range: a row scanned in a bucket the stop emptied is not one of them.
- **Incomplete answers**: `BlocksFailed` (stored blocks of hours not merged that could not be read) and `BatchesFailed` (batches of merged hours that could not be read) are counted as in `Read`; `IsIncomplete` is true when either is above 0 or `CompleteToTicks` is set. The rows of `Rows` are the same rows; the counters of `TableChart` are the ones to show.
- **Errors**: an unknown id, a sensor that is not a table sensor, `from` not before `to`, a bucket count out of range, a column that is not a value column or is named twice: an error result (the request is wrong). A store that cannot be used throws `TableBlockStoreException` (the caller answers 5xx). A store that changed under every plan of the read throws `TableReadChangedException` (below; the same request later is answered). A cancelled token throws `OperationCanceledException`, checked before every block and every merged hour; a chart cancelled while it waited for the gate reads nothing of that source (its read checks the token before its first block or merged hour).

### The hour gate: held per source, not for the range

`TableHourGate` keeps the deletion of a merged hour's blocks out of a read in progress. It is a `ReaderWriterLockSlim`, which queues new readers behind a waiting writer: a read that held it for seconds would stall every other table read as soon as the merge's deletion asked for it.

So the chart holds the gate per source. Its plan (which hours from blocks, which from batches, in which order) is taken outside the gate.

**The plan is one moment of the store.** The batch store keeps a generation per product (`ITableBatchStore.GetGeneration`), a number it changes under the product's lock with every change of what a plan can see: every commit of an hour (one whose every row expired or was dropped, which commits no batch class and appears in no list of merged hours, included), every week database created or dropped, every placement written (#1850 part 4b: a placement gives the class that holds a sensor's rows of each hour), hours forgotten, a rebuild of the hours database or of the placements, a write of moved batches. The plan reads the generation first, then the merged hours and the week databases, the blocks, whether each hour of a candidate block is committed, the sensor's placement, and the generation again last. The generation is the only test of staleness: no list is compared. Equal readings: nothing changed while the plan was taken, so an hour it reads from batches was committed before the blocks were listed (its batches hold nothing newer than the listing), an hour it reads from blocks was not committed when it was asked, and an hour found committed is in its list of merged hours, read from the class its placement gives, whose database of the week is in the plan's list (an hour keeps every class of its record after one of them is dropped, so the hours alone do not show a drop). Different readings: the plan is stale and is taken again. Comparing two lists of merged hours, as the plan did before, was not enough (the third review): an hour committed after the list and before the question was skipped as merged and had no batches in the list; a commit without any class changed no list; a commit followed by the drop of its week could give the same list again. `Read` takes the plan in the same way (it holds the gate, so for it only a commit or a drop can come between the steps) and never reads a stale plan: after `MaxChartPlans` (3) stale plans it throws `TableReadChangedException`, which the read route answers `503` with `Retry-After: 1` (`aicontext/features/api/wire-contract/feature.md`, "Table history"). Wherever either reader is about to plan again or to throw that fault, a cancelled token is checked first: a cancelled read or chart throws `OperationCanceledException`, never the store-changed fault.

Under the gate, before it reads a source, the chart reads the generation again (`ChangedSincePlan`; #1850 part 4b, before that it asked only whether the hour of a source of blocks had been committed). If it is not the plan's, the plan is stale: an hour of its blocks may have been committed (the hour's blocks may be gone; its batches may hold rows the plan did not know, earlier ones too, which would make the plan's order of sources, and so `CompleteToTicks`, wrong; and the commit's record may list blocks the merge kept as unreadable), a week database of the plan may have been dropped (the rest of that week would read nothing: an answer with part of a week), or a placement may have been written (an hour's rows may be in another class). The chart then drops everything it has accumulated and plans again (test seam `TableBlockService.BeforeChartReplan`, called with the hour of the source it was about to read). The same check runs before the stop decision that names a source, so the scan limit never stops before an hour of blocks merged since the plan. The price of the one test: a change that does not touch the chart's sources (the commit of another hour of the product) also makes it plan again. Every answer comes from one plan, whose hours are each read from the source that plan chose:

- an hour still not committed keeps its blocks while the gate is held, since its blocks are deleted only after its commit, and only under the write side of the gate;
- a source decided as batches in the plan is unaffected by deletions of blocks, and its week database is dropped only under the write side of the gate, with a change of the generation the next source's check sees.

After `TableBlockService.MaxChartPlans` (3) plans that were each stale or met a change, the chart gives up with `TableReadChangedException`: never a partial answer. The generation of a product changes about once an hour (its commit; a period change or a drop by retention more rarely), so a second plan meeting a change is rare and a third rarer; a catch-up merge of many hours of the product, and the move between period classes once it exists (#1850 part 4c: a placement written per moved run), change it far more often, and a chart of that product may then end in the fault until they are done.

What a chart answers for an hour committed with a block the merge kept unreadable: exactly what a chart planned after the commit answers. It reads the hour from its batches, the rows of the readable blocks, and the kept block is counted in `BlocksFailed` (#1850 part 4b), as before the merge: the answer is incomplete. `Read` counts it the same way.

**The plan and placements (#1850 part 4b).** The plan takes the class of each merged hour from the sensor's placement (`ClassOf`), selects an hour by the union of its record's class ranges, and leaves out an hour whose class database of its week no longer exists. A plan is a snapshot and is never patched: a week database dropped since the plan changes the generation, so the chart plans again before its next source (as after a commit), and a chart never shows part of a dropped week. The chart's own look at the week databases before a source of batches, which the first version of part 4b had, is gone: `DropWeek` changes the generation. `Read` plans and reads under the gate for its whole range, and retention drops a week under the write side of the gate, so `Read` sees a week whole or not at all. Pinned by `TableChartReadTests.A_block_kept_unreadable_is_a_failed_block_before_and_after_its_hour_is_merged`, `Both_readers_see_the_rows_merged_under_a_period_before_its_change`, `A_week_dropped_after_the_chart_planned_it_is_planned_again_and_left_out_whole`, `A_batch_whose_record_survives_without_its_bytes_is_a_failed_batch_for_both_readers`, `A_placement_written_or_hours_forgotten_during_a_plan_make_it_stale`, `A_placement_written_after_the_chart_planned_is_planned_again`.

Pinned by `TableChartReadTests`: `An_hour_committed_and_deleted_between_the_plan_and_its_read_is_read_from_its_batches` (one more plan, the hour from its batches); `Rows_that_reach_an_hour_after_the_plan_never_let_the_answer_certify_past_them` (the review's sequence: scan limit 1, the plan sees an open hour whose earliest row is at 80; rows at 10 and 0 arrive and both hours commit; the answer counts the row at 0 and certifies only up to the bucket of 10); `An_hour_committed_with_a_block_kept_unreadable_after_the_plan_is_answered_as_a_chart_planned_after_the_commit`; `A_commit_under_each_of_three_plans_ends_in_the_store_changed_fault`; `A_chart_cancelled_while_it_waited_for_the_gate_does_not_read_the_source`; `A_merge_between_the_steps_of_a_plan_never_lets_the_answer_certify_past_a_row` (the second review's sequence, a merge between the two lists or after the listing, with the deletion delay of a day and of 0; each case fails without the stale check); `A_new_plan_after_sources_were_read_counts_their_rows_once`.

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
| 100 merged hours of one table, 1,000 buckets, bid and ask (2,500 batches) | 10,000,000 | 201–246 ms | 710 MB |
| One hour not merged: 360 blocks of 1,000 tables, 167 rows of the table and 7 of every other table in each, 1,000 buckets | 60,120 | 53–74 ms | 28 MB |
| For comparison, `Read` of the same merged range (the default limit) | 1,000,000 | 168–175 ms | |
| For comparison, `Read` of the same unmerged hour | 60,120 | 58–62 ms | |

- The allocation is transient, about 71 bytes per row read (284 KB per batch of 4,096 rows), and it is the decoding of batches, not the reducer: the decoded arrays (24 bytes a row for time, bid, ask), the mantissa arrays of the double columns, and a new zstd `Decompressor` per batch (`TableBatchCodec.Decompress`), whose context is likely most of the rest (not profiled). Dropping the rows of the rows mode before they are copied did not change it (709.9 MB against 710.1 MB: in this range the rows mode ends in the first batch). A decompressor reused per thread would be a small change in the shared codec; it is not made here. The reducer itself keeps a few arrays of the bucket count.
- The times vary between runs on this shared machine: the second set (after the fixes of the review) was 246 ms and 74 ms, with `Read` at 258 ms and 80 ms in the same run.
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

- `HSMServer.Core.Tests/TableSensors/TableChartReducerTests.cs`: the reduction against a brute-force reference (rows out of order, equal times, nulls, NaN, infinities, int64 and double, several batch and bucket counts); an empty range; the bucket edges (first tick, last tick, the end outside, a one-tick range, a range not divisible by the bucket count, more buckets than ticks, the widest `DateTime` range); nulls, NaN and infinities counted; int64 beyond 2^53 exact; the columns asked for in their order; rows mode and its end; no rows in an answer with a failed batch; the scan limit (complete up to a bucket boundary, everything after it empty); the first and the last row of the range (rows out of order, an empty range, after a stop: the rows counted); a damaged batch; the configuration's default and its validation.
- `HSMServer.Core.Tests/TableSensors/TableChartReadTests.cs`: a range over hours not merged and merged, the same before and after the merge; an hour committed and deleted between the plan and its read; a short range carries the rows exactly as `Read` returns them; the scan limit across arrival hours; cancellation between blocks and after waiting for the gate; a new plan after a commit (the review's sequence, a block kept unreadable, three commits in a row); the refusals of a request; the plan at one generation of the store (the third review): `Read` whose three plans each meet a commit between its block listing and its question ends in `TableReadChangedException`, and one whose first plan met a commit reads the hour once from the second; a commit without any class during the plan of `Read` and of the chart (the answer has the row that arrived in a later hour); a commit and the drop of its week during a plan make it stale (the hour stays committed after the drop, #1850 part 4b, and the read answers no row of the dropped week); a placement written or hours forgotten during a plan make it stale, and a placement written after a chart's plan makes it plan again; a read cancelled during its last stale plan and a chart cancelled while its third plan waited for the gate throw `OperationCanceledException`. Each of these fails with its product change reverted.
- `HSMServer.Core.Tests/SensorApi/TableReadChangedRouteTests.cs`: the read route answers a store that changed under every plan with `503`, `Retry-After: 1` and its text.
- `HSMServer.Core.Tests/TableSensors/TableChartBenchmark.cs`: the measurement above (skipped unless `HSM_TABLE_STORE_BENCH=1`).

## Known Issues / Limitations

- `Read` holds the hour gate for its whole range (about 170 ms for 1,000,000 merged rows; about 60 ms per unmerged hour of the measured shape, so a read of a range with several unmerged hours holds it for that many times 60 ms). While it does, a deletion of merged blocks waits, and every read that starts after the deletion asked for the gate waits behind it.
- Blocks never merged that retention deletes as expired (`DeleteMergedHour` of an hour that was never committed, under the write side of the gate) change no generation: a chart that planned such an hour from its blocks and reaches it after the deletion counts them in `BlocksFailed` and is marked incomplete (their period had passed).
- The plan keeps the index entries of its blocks for the length of the read, and a merged hour's batches are read together before the scan limit is checked.
- The scan limit is checked between sources: one source can take the count past it.
