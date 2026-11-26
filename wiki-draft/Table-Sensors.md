# Table Sensors

> **Draft, not released yet.** This page describes the table sensors branch (`feature/1842-table-sensors`, epic #1842). The released server 3.44.0 has no table sensors; the first release with them is later than 3.44.0. Where the branch is not finished, the page says so.

This page is for a user of the HSM site: what a table sensor is, what its page shows, how long its rows are kept, how to watch it and how to read its rows over the REST API. How to send rows from an application is on [DataCollector Table Sensors](DataCollector-Table-Sensors). All kinds of sensors are compared on [Sensor Types](Sensor-types).

---

## What a table sensor is

An ordinary sensor holds **values**: one number (or text, or bar) at a time, with a status and a comment. A **table sensor** holds **rows**:

- every row has a **time** (UTC, to 100 nanoseconds) and the same **columns**, fixed when the sensor is registered;
- a column holds 64-bit integers (`Int64`) or doubles (`Double`);
- any cell except the time may be **empty** (no value);
- rows may arrive out of time order, and several rows may have the same time.

The set of columns with their names and types is the sensor's **schema**. A quote feed, for example, is a table sensor with the columns `time`, `bid` and `ask`.

**When to use one.** Use a table sensor when you have many rows a second that belong together (quotes, ticks, samples of several measurements taken at the same moment) and you want to keep and look at them, not to alert on each value. A table sensor takes tens of thousands of rows an hour and stores them compactly. Use ordinary sensors when you want a status, alerts or an Inactivity Period on the values themselves, or when a value comes every few seconds or less often.

**Where one comes from.** A table sensor is created only by a collector (or by the registration route of the Sensor API), with its schema: there is no way to create one on the site. It appears in the tree like any sensor, at its path in its product. The tree shows it with an empty circle and "no data" in its tooltip, because it has no values.

---

## The sensor page

Select the sensor in the tree. The page shows, from top to bottom:

**General info.** "Sensor type: Table", "Sensor status: Empty" (a table sensor has no status), the creation date and the description the collector sent.

**Cleanup.** The **Keep sensor history** row, with this note under it:

> Rows of a table sensor are kept by whole weeks: the period is rounded up to whole weeks (10 days are kept as 2 weeks), and rows are deleted a week at a time, so they may stay up to a week longer than the period.

See [How long rows are kept](#how-long-rows-are-kept). The page also shows **Remove sensor after inactivity**, as for every sensor, but it has no effect on a table sensor: a table sensor is never removed for inactivity.

**Table schema.** One line of text, then the columns:

> A table sensor: its data is rows, sent in blocks. Schema id `0x1D51EF18`. It has no values, statuses or alerts; it cannot be moved or renamed.

| # | Column | Type |
|---|---|---|
| 0 | time | Time |
| 1 | bid | Double |
| 2 | ask | Double |

The schema id is a checksum of the columns: two sensors with the same columns have the same id.

**Rows.** The chart and the download:

| Control | What it does |
|---|---|
| **Range:** Last hour (the default), Last 3 hours, Last day, Last week, Custom | The time range of the chart. A preset ends at the moment you choose it. **Custom** shows **From:** and **To:** (read as UTC) and a search button, "Show this range". |
| **Columns:** one checkbox per column, with its type | Which columns are drawn. Double columns are on by default, Int64 columns off. A change keeps the zoom. With no column chosen the page says "Choose a column to draw." |
| The chart | A zoom (drag on the plot) asks the server for the zoomed range and shows **Reset zoom**. **Reset zoom**, or a double click on the plot, returns to the chosen range. The time axis always covers the asked range, so a part without rows shows empty. The hover and every time on the page are UTC. |
| **Download CSV** | Downloads the rows of the shown range (the zoom, if there is one). See [Download as CSV](#download-as-csv). |
| **Feed of this table (rows, bytes, delivery lag)** | A link under the chart, shown once the server has made the sensor's [feed sensors](#watching-a-feed-the-feed-sensors): selects the node `.tables/<the sensor's path>` in the tree. |

### How the chart draws rows

The server does not send every row to the browser. It cuts the range into as many equal **buckets** as the plot is wide in pixels (100 to 2,000) and sends, per bucket and column, the smallest and the largest value. The chart draws each column as a band between the minimum and the maximum of each bucket. A bucket without a value is a gap.

When the range holds at most twice as many rows as buckets, the server sends the rows themselves and the chart draws one line through them: zoom into a few seconds to see every row. Points are drawn when a line has at most 300 of them, so a single row stays visible.

An empty cell is never drawn as 0: it is counted and left out. A double that is NaN or an infinity is counted and not drawn.

### The lines above the chart

| Line | When |
|---|---|
| `600 rows, the first at 2026-11-26 00:01:54.8625403, the last at 2026-11-26 00:11:53.8625403 UTC.` | Always: the rows of the shown range, with the exact times of the first and the last one. |
| `No rows in the range.` | The range holds no row; the plot says "No rows in the range". |
| `1 row had no value in ask.` | (grey) Per column with empty cells in the range. |
| `The rows of 2 damaged blocks and 1 damaged batch are missing.` | (warning) Some stored data of the range could not be read. The rest is drawn. Asking again gives the same answer: the data is damaged, and the server's own sensors report it (see [The server's own sensors](#the-servers-own-sensors)). |
| `Drawn only up to 2026-11-26 10:00:00 UTC: the range holds more rows than one chart reads; narrow the range.` | The range holds more rows than one chart reads (10,000,000 by default, `TableSensors.MaxChartScanRows`). Everything before that time is complete; after it the chart is empty. Choose a shorter range. |
| `3 values of bid were not finite (NaN or infinity) and are not drawn.` | Such values exist in the range. |
| `The axis of size is approximate: some of its values are beyond 2^53 and are drawn rounded.` | An Int64 column holds values a browser cannot draw exactly. The numbers are exact in the download and the REST API. |

### Errors on the page

| What you see | What it means, what to do |
|---|---|
| `This table sensor no longer exists or is not visible to you.` | The sensor was removed, or you have no access to its product. |
| `From must be before To: the range is [From, To).` | A custom range whose From is not before its To. Fix the range. |
| `The server is busy, trying again in 1 s (attempt 1 of 3)…` | The server answered 503. The page tries again by itself up to 3 times, waiting as long as the server says (1 to 120 s). The usual reason is "The table store changed during the read (hours were merged meanwhile); try again.": the server was storing an hour of rows in its compact form while it read (see [When rows become visible](#when-rows-become-visible)). |
| `The server is still busy: <text>` and **Retry** | The three tries did not help. Click **Retry** later. "The table block store cannot be read now." means the server cannot open its table store; look at the server's log. |
| `The server did not answer in time.` and **Retry** | No answer within 120 s. |
| `Your session has ended.` and **Sign in** | Your sign-in expired. Sign in again; the page does not lose anything. |

### Download as CSV

**Download CSV** saves the rows of the shown range (the zoom, if any) as a file named after the sensor's full path and the range, for example `TableDocs_quotes_EURUSD_from_2026-11-25T23_13_12_to2026-11-26T00_13_12.csv`. The file:

- starts with one header line of the column names, the time first: `time,bid,ask`;
- has one line per row, **sorted by time**; rows with the same time keep the order in which they arrived;
- writes a time as ISO 8601 UTC with seven fraction digits (`2026-11-26T00:01:54.8625403Z`);
- writes a double as the shortest text that reads back to the same value (`1.0800166658950725`), NaN and the infinities as `NaN`, `Infinity`, `-Infinity`, an Int64 exactly;
- leaves an **empty cell empty** (`2026-11-26T00:06:54.8625403Z,1.079041075725337,`);
- uses commas, CRLF line ends and RFC 4180 quoting (a name with a comma or a quote is quoted).

```
time,bid,ask
2026-11-26T00:01:54.8625403Z,1.08,1.0802
2026-11-26T00:01:55.8625403Z,1.0800166658950725,1.0802166658950725
...
2026-11-26T00:06:54.8625403Z,1.079041075725337,
```

A range without rows downloads the header line only.

Limits and refusals (the page shows the text under the buttons, as `The download was refused (<status>): <reason>`, and stays as it is):

| Case | What happens |
|---|---|
| The range holds more than 10,000,000 rows (`TableSensors.MaxDownloadRows`) | The button is disabled (its tooltip says why) when the chart already counted more; otherwise the server refuses with 400: "The range holds more than 10,000,000 rows (TableSensors.MaxDownloadRows); download a shorter range." |
| Stored data of the range cannot be read | 500: "The range cannot be downloaded whole: …" (the server does not hand out a file with rows missing). |
| The server is busy or the data changed during the read | 503: "The server is busy; try again in N s." |
| A reversed custom range | 400: "From must be before To: the range is [From, To)." |
| Your sign-in expired | "The download did not start: your session has ended." and **Sign in**. |

When the download has started, the page says "The download has started: the browser saves the file." and the browser saves it as it arrives, so a large file does not fill the browser's memory. If a problem is found in the middle of a large download, the server breaks the connection and the browser shows a **failed** download: a file that finished is always complete.

A download is read part by part from the live store. Rows that arrive **during** the download with a time earlier than the part being read are not in the file. A range that ended before you clicked is complete.

---

## When rows become visible

1. The collector collects rows in memory and sends them **once per tick, every 10 seconds** by default (the collector's `TableTickPeriod`). All its table sensors travel in one message.
2. The server stores the message as it arrives and answers the collector. From then on the rows are in the chart, the download and the REST API.
3. **Once an hour** the server stores the rows of each closed hour in a compact form. This "merge" starts about 2 minutes after the end of the hour (the server looks once a minute). What you see does not change: before and after the merge the same rows are shown in the same order. During the merge a read can be answered "the data changed, try again" (503); the page and the REST clients simply try again.
4. The rows as they arrived are kept for another 24 hours after the merge (`TableSensors.DeleteMergedBlocksAfterHours`), as a safety margin, and then deleted.

So a row is normally visible about one tick (10 s) after the application added it. If rows do not appear, look at the collector first (see [DataCollector Table Sensors](DataCollector-Table-Sensors#when-something-goes-wrong)), then at the feed sensors of the product.

---

## How long rows are kept

A table sensor uses the same **Keep sensor history** setting as every sensor: its own, or the one it inherits from its folder and product (a new product starts with 1 month). A collector may also send the setting when it registers the sensor.

For a table sensor the period is counted in **whole weeks**, rounded up:

| Setting | Kept for |
|---|---|
| 1 week | 1 week |
| 10 days (Custom) | 2 weeks |
| 1 month | 5 weeks |
| 3 months | 14 weeks |
| 6 months | 27 weeks |
| 1 year | 53 weeks |
| 5 years | 261 weeks |
| Forever | forever |

Rows are stored by week and deleted a whole week at a time, once the period has passed **after the end of the row's week**. A row is therefore kept at least for the period and at most one week longer. The server checks once an hour.

One exception, a known issue of this branch that is not decided yet: a table sensor that takes "1 month" (or another preset) from a **folder** is currently kept for ever, as an ordinary sensor's history is in that case.

**Changing the period.**

- A **longer** period protects the sensor's rows from the moment you save it, including rows of weeks that would have expired under the old period, as long as they were not deleted yet. Rows already deleted do not come back.
- A **shorter** period deletes the rows it no longer covers: a few minutes after the change, rows of the weeks for which the new period has already passed are deleted, and the sensor's newer rows follow the new period from then on.
- In both cases the server then moves the sensor's stored rows, in the background, into the files of the new period, so that each week file holds only rows of one period and is deleted whole when its time comes. The move runs once a minute for at most 10 seconds and 256 MB of rows (the server settings `TableSensors.MoveSecondsPerPass` and `MoveMegabytesPerPass`); moving a year of a busy table took about an hour in our measurement. A move never changes what a read of the sensor returns, apart from the rows a shorter period deletes.
- Rows of the current hour, and rows not merged yet, are moved after they are merged (within the next hour). While a product's merge lags behind, its rows wait for it.
- Deleted rows give their disk space back when a tenth of their week file is deleted (the file is then compacted) or when the whole week file is deleted.

The server's sensors under `Database/Tables` show the move: `Move steps pending` (sensor-weeks still in the files of an old period), `Moved rows`, `Rows expired by the move`, and `Move faults` (see "The server's own sensors" below).

**Removing a table sensor** (Remove in the tree) removes the sensor. Its rows are no longer shown and are not given to a new sensor created later at the same path. Its stored rows are deleted too, within a few minutes, whatever its period, as the history of any sensor is deleted with it; there is no undo. Removing a folder deletes the rows of every table sensor in it, and removing a product deletes all its table rows, including the rows it received in the last hours that were not yet merged. If the server stops in the middle, it finishes the deletion after it starts again. The server sensor `Orphan table placements` (under `Database/Tables`) counts the rare table sensors whose rows could not be scheduled for deletion; those rows are kept and named in the server log once a day. If the sensor's collector is still running, it registers the sensor again, as a new, empty sensor.

---

## What a table sensor cannot do

| Operation | What happens |
|---|---|
| Alerts, alert templates | Not offered: the page has no alerts block, the alert template form does not list the type, and the management API refuses them. Put alerts on the [feed sensors](#watching-a-feed-the-feed-sensors). |
| Inactivity Period (TTL) | None, and it does not inherit one from its folder. Use a TTL on the feed sensor `Rows`. |
| Status, value edit | It has no status or value. "Edit status" shows no value field. |
| Move, rename, merge with another sensor | Refused (the management API answers `notSupported`). Another place or another shape is another sensor: register a new path. |
| Rename a node above it | Refused: no node below the product's root can be renamed on this server (its name is part of its sensors' paths). Renaming the **product** itself is allowed. |
| Change its columns | Refused: a registration of the same path with other columns is answered "Table sensor '…' is registered with the schema (time Time, bid Double, ask Double); a registration with (time Time, bid Double) is refused: a different shape is a different sensor." |
| Clear history | Refused for the sensor; a clear of a node skips it. |
| Aggregate values | The switch is hidden. |
| Remove for inactivity | Never. |

Grafana and the history routes of ordinary sensors (`api/sensors/history`, `historyFile`) do not return table rows: use `tableHistory` (below).

---

## Watching a feed: the feed sensors

A table sensor has no alerts. To watch a feed, the server keeps ordinary sensors in the same product, under the node **`.tables`**, made from what it stores. They appear with the first stored rows. You set alerts and Inactivity Periods on them like on any sensor; the server sets none.

| Sensor (in the product) | Kind, unit | Posted | Value |
|---|---|---|---|
| `.tables/Rows` | Integer, count | every minute with stored rows | Rows of all table sensors of the product stored in that minute. |
| `.tables/Stored` | Double, MB | every minute with stored rows | Bytes stored in that minute (3 decimals). |
| `.tables/Delivery lag` | Double, seconds | every minute with stored rows | The largest, in that minute, of "time the server received a message (before writing it)" minus "time of its last row". It includes the difference between the clocks of the sender and of the server, and can be negative. |
| `.tables/Tables refused` | Integer, count | in a minute with refusals, then `0` once | Tables of incoming messages the server refused (no table sensor at the path, another schema, a blocked sensor, ...). Their rows are not stored. |
| `.tables/Blocks the merge could not read` | Integer, count | in a minute with such blocks, then `0` once | Stored data the hourly merge found damaged. |
| `.tables/<the table sensor's path>/Rows` | Integer, count | every 5 minutes with rows of that sensor | Rows of that one table sensor. |

**Silence means no data.** Nothing is posted in a minute (or 5 minutes) without rows. So:

- a feed that stopped: an Inactivity Period on `.tables/Rows` or `.tables/<path>/Rows` longer than its period;
- a feed that thinned: an alert on `Rows` below a number;
- late data: an alert on `Delivery lag` above a number of seconds;
- a broken registration: an alert on `Tables refused` above 0;
- damaged storage: an alert on `Blocks the merge could not read` above 0.

What we saw after one collector sent 600 rows: `.tables/Rows` = 600, `.tables/Stored` = 0.009 MB, `.tables/Delivery lag` = 10.339 s (the rows were added one tick before they were sent).

The feed never changes a sensor that exists: your description, unit, alerts and TTL stay. A feed sensor you remove comes back with the next rows. The periods are the server's settings `TableSensors.FeedProductPeriodMinutes` (1) and `FeedTablePeriodMinutes` (5); `FeedTableSensors` = false switches the per-sensor `Rows` off. A table sensor whose path has more than 8 parts has no `Rows` sensor of its own (a path has at most 10 parts); its rows still count in `.tables/Rows`.

---

## The server's own sensors

The server reports its table store in its own product, **HSM Server Monitoring**, under **`Database/Tables`**, every 5 minutes. They appear at the next 5-minute sample after the table store is first opened in the server's run (the first registration of a table sensor, or the first rows); the merge and retention sensors (from `Batch databases open` down) appear after the first hourly merge. Most are counters since the server started; `Blocks in store`, `Batch databases open`, `Batch week databases`, `Merge lag`, `Merged hours holding blocks`, `Move weeks pending`, `Move steps pending`, `Move steps blocked` and `Move steps refused now` show the current state.

| Sensor | Means trouble when |
|---|---|
| `Blocks stored`, `Rows stored`, `Stored` (MB), `Blocks in store` | — (volume) |
| `Duplicate blocks`, `Duplicates by watermark` | — (a collector sent a message again after a lost answer; nothing is stored twice) |
| `Tables refused`, `Blocks not stored` | it grows: collectors send rows of sensors that do not exist or have another schema; the server's log has the reasons |
| `Invalid blocks` | Error: a collector sent data that breaks the format or the server's limits |
| `Write failures` | Error: rows could not be stored (disk full, below `TableSensors.MinFreeDiskMb`, storage error); collectors keep them and send again |
| `Read failures` | Error: stored rows could not be read |
| `Index rebuilds` | it grows: the store's index was rebuilt at a start |
| `Batch databases open`, `Batch week databases`, `Merged hours` | — |
| `Batch write failures`, `Batch read failures` | Error |
| `Arrival hours merged`, `Rows merged`, `Last merge duration` (ms), `Merged blocks size`, `Merged batches size` (MB) | — (the hourly merge) |
| `Merge lag` | above 0 for long: closed hours wait for the merge |
| `Verification mismatches` | Error, must stay 0 |
| `Unreadable blocks kept` | Error: damaged stored data |
| `Rows of removed sensors dropped`, `Rows expired before the merge` | — |
| `Merged hours holding blocks` | — (hours whose original data waits for its 24 hours) |
| `Batch weeks dropped`, `Expired blocks deleted`, `Expired block rows deleted` | — (retention) |
| `Retention faults` | Error: a week could not be deleted; tried again an hour later |
| `Move weeks pending` | — (week files kept because a sensor whose period changed still has rows in them; they go once the move is done) |
| `Move steps pending` | stays above 0 for hours: sensors whose period changed are not moved |
| `Move steps blocked` | above 0 for long: the move cannot run for some products (their merge lags, or `Move faults`) |
| `Move steps refused`, `Move steps refused now` | it grows: a step could not run (too little free disk, damaged data); the rows stay where they are and are read there; the log has the reason |
| `Moved rows`, `Moved batches size` (MB), `Rows expired by the move`, `Move leftovers cleaned`, `Batch databases compacted` | — (the move) |
| `Move faults` | Error: a step of the move or a compaction failed; tried again a minute later |

The server's log (`Logs/HSM-all-log.txt`, `Logs/HSM-error-log.txt`) has a line when the store opens (`Table block store Databases/Tables opened in 59 ms: 0 blocks, next sequence 0`) and one per table connection of a collector (`Collector table connection … of session … opened: product "TableDocs", key "DefaultKey", client "table-quick-start", …`). Refused tables are summarised in a warning at most once a minute. The Configuration page shows the size of the table store as "Table blocks (MB)".

---

## Reading rows over the REST API

`POST api/sensors/tableHistory` returns the rows of one table sensor in a time range. It takes the access key in the `Key` header (only there); the key needs the permission to read sensor data (`CanReadSensorData`; a product's `DefaultKey` has it). The path is the sensor's path in the key's product.

```bash
curl -X POST "https://hsm.example.com/api/sensors/tableHistory" \
  -H "Key: <access key>" -H "Content-Type: application/json" \
  -d '{"Path":"quotes/EURUSD","From":"2026-11-26T00:00:00Z","To":"2026-11-26T01:00:00Z","Format":1}'
```

| Field | Meaning |
|---|---|
| `Path` | The sensor's path in the product of the key (`/quotes// EURUSD` finds `quotes/EURUSD`). |
| `From`, `To` | The range `[From, To)`: From included, To excluded. UTC; a time without a zone is UTC. |
| `Format` | `0` JSON (default), `1` CSV, `2` table blocks (for the collector library). |
| `MaxRows` | Optional. The most rows of the answer, capped at 100,000 for JSON and CSV (`TableSensors.MaxTextReadRows`) and 1,000,000 for blocks (`TableSensors.MaxReadRows`). |

**CSV** is the file of the [download](#download-as-csv) (`text/csv; charset=utf-8`).

**JSON** (with `"MaxRows":3`):

```json
{"path":"quotes/EURUSD","columns":[{"name":"time","type":"time"},{"name":"bid","type":"double"},{"name":"ask","type":"double"}],"rowCount":3,"truncated":true,"nextFrom":"2026-11-26T00:01:57.8625403Z","rowsAtTimeOmitted":false,"blocksFailed":0,"batchesFailed":0,"rows":[["2026-11-26T00:01:54.8625403Z",1.08,1.0802],["2026-11-26T00:01:55.8625403Z",1.0800166658950725,1.0802166658950725],["2026-11-26T00:01:56.8625403Z",1.0800333271608369,1.0802333271608369]]}
```

An empty cell is `null`; NaN and the infinities are the strings `"NaN"`, `"Infinity"`, `"-Infinity"`.

**Headers of every answer:**

| Header | Meaning |
|---|---|
| `X-Hsm-Row-Count` | Rows in this answer. |
| `X-Hsm-Truncated` | `true` when rows of the range were left out of this answer. |
| `X-Hsm-Next-From` | Present when there is a next page: send it as `From` to continue. Every row comes exactly once. **Page until an answer has no `X-Hsm-Next-From`.** |
| `X-Hsm-Rows-At-Time-Omitted` | Present (`true`) only when one time has more rows than `MaxRows`: the rest of that time is skipped, and the next page starts one tick later. Ask with a larger `MaxRows` to get them. |
| `X-Hsm-Blocks-Failed`, `X-Hsm-Batches-Failed` | Stored data of the range that could not be read. **When either is not `0` the answer is incomplete**: report it, do not take the rows for the whole range. Asking again gives the same answer. |

A page never ends in the middle of the rows of one time (apart from the case above).

**Errors** (plain text):

| Status | Text (examples) |
|---|---|
| 400 | `From must be before To: the range is [From, To).` · `The request has no path.` · `MaxRows must be positive.` · `The format 7 is unknown: 0 JSON, 1 CSV, 2 blocks.` · `Sensor "a/b" is not a table sensor.` |
| 404 | `No sensor "quotes/nothing" in the product of the key.` |
| 406 | `An access key is required`, or the key may not read. |
| 503, `Retry-After: 1` | `The table store changed during the read; the request can be sent again.` Send it again after a second. |
| 503, `Retry-After: 60` | `The table block store cannot be read now.` |

At most 2 table reads run at once on the server (`TableSensors.MaxConcurrentReads`, shared with the site's charts and downloads); others wait for their turn.

---

## Server configuration

The section `TableSensors` of `Config/appsettings.json`. The server writes the defaults into the file at its first start and checks every value at startup (a wrong value stops the start with a message that names the key). There is no switch: the feature does nothing until a table sensor exists.

| Key | Default | Bounds |
|---|---|---|
| `MaxDecompressedBytes` | 67,108,864 (64 MiB) | the size of one message of rows, unpacked |
| `MaxTables` | 65,536 | table sensors in one message |
| `MaxRows` | 8,388,608 | rows in one message |
| `MaxColumnsPerTable` | 64 | columns of a schema, the time included |
| `MaxTableIdLength` | 1,024 | bytes of a table sensor's path (UTF-8) |
| `MaxReadRows` | 1,000,000 | rows of one read (REST blocks format, one time of a download) |
| `MaxTextReadRows` | 100,000 | rows of one JSON or CSV answer, and of one part of a download |
| `MaxConcurrentReads` | 2 | reads at once (REST, charts, downloads) |
| `MaxConcurrentPuts` | 4 | messages of rows stored at once |
| `MinFreeDiskMb` | 1,024 | below this much free disk the server stores no rows (the collectors keep them and send again); 0 turns the check off |
| `MaxOpenBatchDatabases` | 32 | week files of the compact store kept open |
| `DeleteMergedBlocksAfterHours` | 24 | how long rows as they arrived are kept after the hourly merge (0 to 8,760) |
| `MergeMemoryBudgetMb` | 256 | memory of one merge pass |
| `MaxChartScanRows` | 10,000,000 | rows one chart reads before it stops ("Drawn only up to …") |
| `MaxDownloadRows` | 10,000,000 | rows of one download (about 0.5 GB of CSV for time, bid, ask) |
| `FeedProductPeriodMinutes` | 1 | period of the product's feed sensors (1 to 1,440) |
| `FeedTablePeriodMinutes` | 5 | period of each `.tables/<path>/Rows` (a multiple of the above) |
| `FeedTableSensors` | true | false: no `Rows` sensor per table sensor |

In the section `CollectorSessions`: `MaxTableFrameBytes` (4 MiB) is the largest message of rows a collector may send; **0 switches table connections off**, and collectors then deliver no rows. `MaxTableBytesInFlight` (256 MiB) bounds the memory of messages being received.

---

## Limits

- Columns: 1 to 63 value columns besides the time (64 with it, `MaxColumnsPerTable`); names of 1 to 255 bytes, unique, not `time`.
- Path: at most 1,024 bytes and 10 parts; the path is stored without empty parts or spaces around them (`a/b`, not `/a/ b`).
- Time: 100 ns precision, UTC.
- Rows of one collector message: at most 8,388,608 rows and 64 MiB unpacked; a collector splits larger amounts by itself.
- Chart: up to 2,000 buckets; 10,000,000 rows read per chart.
- Download: 10,000,000 rows. REST: 100,000 rows per JSON or CSV page.
- Retention: whole weeks; rows may stay up to a week longer than the period.

---

## Try it

From an empty server to rows on the chart and in a file, without writing code. You need Docker Desktop, the .NET 8 SDK, Node 20 and `curl`, and a clone of the repository with the branch `feature/1842-table-sensors`. The version for a developer, with a C# program, is in [DataCollector Table Sensors](DataCollector-Table-Sensors#try-it).

**1. Run a server built from the branch.** From the repository root, in PowerShell:

```powershell
.\scripts\local-docker-build.ps1 -Branch feature/1842-table-sensors -ImageTag tables
```

It checks the branch out, builds the server into the image `hsmonitoring/hierarchical_sensor_monitoring:tables` and starts it with Caddy at `https://localhost` (port 443). Expect "HSM Server is running". Failure: the script stops with the failing step (`dotnet publish failed`, `docker build failed`); a warning that `gh` is missing only means the agent download page will not work, which this walk-through does not need.

*If your machine already runs a container named `hsm-server` or uses port 443*, do not use the script, which replaces that container. Build the image yourself and start it under another name and port: `dotnet publish src/server/HSMServer/HSMServer.csproj -c Release --os linux --arch x64 -o <folder>`, `docker build -t hsm-tables:local -f docker_scripts/HSMserver/Dockerfile.local <folder>`, then a compose file of your own with `name: hsm-tables`, `container_name: hsm-tables`, `image: hsm-tables:local` and `ports: ['127.0.0.1:18443:443']`, started with `docker compose up -d` in its own folder. The steps below then use `https://localhost:18443`.

**2. Check that it answers.** `curl -k https://localhost/api/sensors/testConnection` prints nothing and returns 200 (add `-w "%{http_code}"` to see it). Failure: connection refused (the container is still loading: wait, `docker ps` shows `healthy`), or 502 from Caddy (the server is not up yet).

**3. Sign in and create a product.** Open `https://localhost` and accept the self-signed certificate. Sign in (see [Installation](Installation) for the first sign-in). Open the products page (`https://localhost/Product/Index`), click **Add product**, type `TableDocs` as the **New product name**, click **Add**.

**4. Copy the access key.** Open the access keys page (`https://localhost/AccessKeys`): the row of `TableDocs` shows `DefaultKey` with the permission **Full**. Copy the key. Below, `KEY` stands for it.

**5. Register a table sensor** with the columns time, bid, ask (types: 1 time, 2 Int64, 3 Double):

```bash
curl -k -X POST https://localhost/api/sensors/addOrUpdate -H "Key: KEY" -H "Content-Type: application/json" \
  -d '{"Path":"quotes/GBPUSD","SensorType":11,"TableColumns":[{"Name":"time","Type":1},{"Name":"bid","Type":3},{"Name":"ask","Type":3}]}'
```

Expect status 200 and an empty body. The tree of `TableDocs` now has `quotes/GBPUSD`. Failure: 406 with a reason (a wrong key, or a schema different from an earlier registration of the same path, as in the table under [What a table sensor cannot do](#what-a-table-sensor-cannot-do)).

**6. Send 200 rows** with the repository's seeding tool, from `src/tests/Autotests/tools/TableSeeder`. It sends one message of rows over the collector session, as a collector does, and waits for the answer. The rows are spread over the last 30 minutes; `--null ask:57` leaves the ask of row 57 empty. The key is read from `HSM_TABLE_SEEDER_KEY`, so it stays off the command line:

```powershell
$env:HSM_TABLE_SEEDER_KEY = "KEY"
dotnet run --project src/tests/Autotests/tools/TableSeeder -c Release -- `
  --url https://localhost --path quotes/GBPUSD --rows 200 --from-minutes 30 --to-minutes 1 --null ask:57 --insecure
```

Expect: `TableSeeder: 200 rows of "quotes/GBPUSD" stored (schema 0x1D51EF18).` and exit code 0. Failure: exit code 1 with the reason, for example `TableSeeder: the server refused the table: quotes/NOPE: UnknownSensor` (no table sensor at that path in the key's product: check step 5 and the path), or `the connection to … failed` (wrong address, or the server is down). Exit code 2: wrong arguments; it prints its usage.

**7. Look at the chart.** Select `TableDocs/quotes/GBPUSD`. Expect the schema with `time`, `bid`, `ask`, the range "Last hour", both columns checked, two bands or lines, the line `200 rows, the first at …, the last at … UTC.` and the grey line `1 row had no value in ask.` Zoom into a few minutes: the chart draws the rows themselves, with a gap at row 57's ask. Failure: "No rows in the range." (the rows are older than an hour: choose "Last 3 hours"; or step 6 failed).

**8. Download.** Click **Download CSV**. Expect "The download has started: the browser saves the file." and a file whose first line is `time,bid,ask`, with 200 lines after it, one with an empty last cell.

**9. Read the rows over the REST API.**

```bash
curl -k -i -X POST https://localhost/api/sensors/tableHistory -H "Key: KEY" -H "Content-Type: application/json" \
  -d '{"Path":"quotes/GBPUSD","From":"2026-11-26T00:00:00Z","To":"2026-11-27T00:00:00Z","Format":1}'
```

Use a range around today. Expect `200 OK`, `X-Hsm-Row-Count: 200`, `X-Hsm-Truncated: false`, `X-Hsm-Blocks-Failed: 0`, `X-Hsm-Batches-Failed: 0`, and the CSV. Failure: `404 No sensor "…" in the product of the key.` (path or key), `400 From must be before To` (the range).

**10. Watch the feed.** Within a minute the product has `.tables` with `Rows` (200), `Stored` and `Delivery lag`; within 5 minutes `.tables/quotes/GBPUSD/Rows`. **HSM Server Monitoring → Database → Tables** gets its sensors at the next 5-minute sample (`Blocks stored`, `Rows stored`, ...; none of them Error).

**11. After the hour.** About 2 minutes after the end of the hour of the rows, the server merges it (`Database/Tables/Arrival hours merged` grows, `Merge lag` is 0). The chart and the download show the same 200 rows as before.

**12. Clean up.** With the script: `docker compose -f docker-compose.yml -f docker-compose.local.yml down` at the repository root, with `$env:HSM_DOMAIN='localhost'; $env:HSM_CERTIFICATE='self-signed'` set (the script prints this line). The data stays in `Databases/` under the repository root; delete that folder to start empty next time.
