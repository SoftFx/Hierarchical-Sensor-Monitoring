# Feature: Feed sensors of table sensors

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the ordinary sensors the server derives from the table blocks it stores, so that users can set alerts and TTLs on a table feed (#1847, step 5 of epic #1842, ADR-0021 points 14 and 15). The table sensor kind is in `aicontext/features/server/table-sensors/feature.md`, the block store in `aicontext/features/storage/table-blocks/feature.md`.

---

## Overview

A table sensor owns no alerts and no TTL: it has no values to judge. To watch a feed, the server keeps **feed sensors** (glossary): ordinary sensors in the table sensor's own product, under the node `.tables`, posted from what the server already knows when it stores a block (its directory and header) and when the hourly merge commits a product's hour. Users of the product see them in its tree and set alerts on them like on any sensor; the server never sets an alert on them.

| Path (in the table sensor's product) | Type | Unit | Period | Value |
|---|---|---|---|---|
| `.tables/Rows` | Integer | Count | product (1 min) | Rows of the accepted tables of the blocks stored in the period. Rows of refused tables are not counted. |
| `.tables/Stored` | Double | MB | product | Stored length of the blocks stored in the period, as stored (3 decimals). |
| `.tables/Delivery lag` | Double | Seconds | product | The largest, over the blocks stored in the period, of arrival time minus the time of the block's last row (the header's `LastTime`). |
| `.tables/Tables refused` | Integer | Count | product | Tables refused in the period, in stored blocks and in blocks every table of which was refused. |
| `.tables/Blocks the merge could not read` | Integer | Count | product | Blocks of the product the merge could not read in the hours it committed in the period (the commit record's `UnreadableBlocks`). |
| `.tables/<the table sensor's path>/Rows` | Integer | Count | table (5 min) | Rows of that table sensor in the blocks stored in the period. |

Each sensor gets a description at its creation that says what it counts, its unit and its period; a user's later edit of it is kept (see "Creation").

## Invariants

- Counts are per period, not since a start: a restart is not a reset, and the first period after a start is whatever passed between the start and the first tick.
- A duplicate block adds nothing. A block every table of which was refused adds its refusals only (no rows, bytes, lag).
- **Silence means no feed.** A product or table without activity in a period posts nothing: `Rows`, `Stored` and `Delivery lag` only for a period in which a block of the product was stored; a table's `Rows` only for a period in which a block carried that table (a table present with 0 rows posts 0). A TTL alert on a `Rows` sensor is how a user watches a feed.
- **The failure counters** (`Tables refused`, `Blocks the merge could not read`) post their count in a period in which it is above 0, then **0 once** at the next period without one, then nothing until the next failure: an alert on "above 0" clears, and a quiet product writes no history.
- **Delivery lag is per product** (the directory has no time per table) and measured against the clock of the provider that wrote the rows, so a clock skew is in it. A negative lag (the provider's clock ahead of the server's) is posted as it is, not clamped.
- Feed sensors are ordinary sensors: never table sensors. A table sensor that stands on a feed path (a table registered as `.tables/Rows`, say) is not shadowed: the server's value for that path is refused (logged once) and the table sensor stays as it is.

## Primary Workflows

1. **Put** (`TableBlockService.Put`): `Match` resolves each table of the block's directory to its table sensor; on the stored outcome the accepted sensors with their directory row counts, the stored length, the refused count and the header's last row time go to `TableFeedCounters.AddStored`; on the all-refused outcome the refused count goes to `AddRefused`. The arrival time is `TableFeedCounters.Clock` (UTC; a seam for tests).
2. **Merge commit** (`TableMergeJob.MergeProduct`, after `WriteHour`): the record's unreadable blocks go to `AddUnreadable`.
3. **Posting** (`TableFeedSensorsService`, a hosted service on the product period's grid): every tick drains the product counters; a tick in a new table period drains the table counters too (the first tick drains both). For each product still in the tree it builds the values above with the tick's time and sends them through `IServerSensorValues.AddServerValuesAsync`. A value whose path has no sensor gets its sensor registered (below) and is sent again.
4. **Removal**: a removed product takes its feed sensors with it (they are its sensors); its counters are dropped at the next tick. A removed table sensor's `Rows` sensor simply goes silent (nothing is counted for it any more) and stays until a user removes it or its self-destroy setting does.

## How a value reaches a user product

The server's own self-monitoring writes into its own product with its own access key. A feed sensor is in a user's product, for which the server has no key, and must not get one. `IServerSensorValues` (`src/server/HSMServer.Core/Cache/`) is the narrow way in:

- Implemented by `TreeValuesCache`, but **not** a member of `ITreeValuesCache`, which controllers and the management API see. It is registered in DI on its own and given only to `TableFeedSensorsService`. No HTTP route reaches it.
- It **creates no sensor and no key**: a value is added only to a sensor that exists (on its path, or through the alias of a moved sensor), has the value's type and is not a table sensor; any other value is answered per path (`IServerSensorValues.NoSensor` for a missing sensor).
- An added value goes the way of every value: the product's queue (`ServerSensorValuesRequest`), the path checks of `BaseUpdateRequest.TryCheckRequest`, `AddValueToSensor` (a blocked sensor takes nothing, rejected values are counted), the history store and the alerts.

## Creation

A feed sensor is registered by `TreeValuesCache.AddOrUpdateSensorAsync` (the registration a collector's `AddOrUpdateSensorRequest` reaches after its key was checked), with an explicit type, its description, its unit and the initiator `System / Table feed sensors`, the first time a value finds no sensor on its path. It inherits its parent's TTL policies like any new sensor, its history setting is the default (no reason to differ: a feed sensor's values are small and ordinary). It is registered only when missing, so a user's alerts, TTL, description or block survive; a feed sensor a user removed comes back at the next period with activity. There is no limit on the number of sensors of a product in the server (only an access key's permissions bound what a collector may create); the feed sensors are bounded by 5 per product plus 1 per table sensor.

A path that is not a valid sensor path is not posted and logged once (`Warn`, once per path): a table sensor's path of more than 8 parts leaves no room for `.tables` and `Rows` within the 10 parts a path may have, so such a table has no `Rows` feed sensor; its rows still count in the product's `Rows`.

## Configuration

Section `TableSensors` (`TableSensorsConfig`, validated at startup with an error that names the key):

| Key | Default | Meaning |
|---|---|---|
| `FeedProductPeriodMinutes` | 1 | The period of the product's feed sensors and the service's tick; 1..1440. |
| `FeedTablePeriodMinutes` | 5 | The period of each table sensor's `Rows`; 1..1440 and a multiple of `FeedProductPeriodMinutes`. |
| `FeedTableSensors` | true | False: no `Rows` sensor per table sensor is created or posted (the product's sensors still are; the counts are drained and dropped). |

## Cost

- Per block: nothing decompressed; one short lock with a dictionary update per accepted table and a few per product, and one small list of the accepted tables.
- History: an ordinary value takes on the order of 120 to 140 bytes in the history store. 1,000 active tables at 5 minutes are 288,000 values a day, about 35 to 40 MB a day before retention. A product feeding all day posts 3 values a minute (more on a failure), about 4,300 values or 0.6 MB a day.
- Memory: the counters of one period; products and tables without activity hold nothing after their period.

## What a user alerts on

- A feed that stopped: a TTL on `.tables/Rows` (product) or `.tables/<table>/Rows` longer than the period.
- A feed that thinned: an alert on `Rows` below an expected count.
- Late data: an alert on `Delivery lag` above a number of seconds (mind the provider's clock).
- A broken registration or schema: an alert on `Tables refused` above 0.
- Damaged storage: an alert on `Blocks the merge could not read` above 0.

## Key Files

| File | What |
|---|---|
| `src/server/HSMServer.Core/TableSensors/TableFeedCounters.cs` | The counters, `TableFeedProductPeriod` |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs` | `Feed`; `Put` and `Match` add to it |
| `src/server/HSMServer.Core/TableSensors/TableMergeJob.cs` | `Feed`; the commit adds the unreadable blocks |
| `src/server/HSMServer.Core/Cache/IServerSensorValues.cs`, `TreeValuesCache.ServerValues.cs` | The keyless value path |
| `src/server/HSMServer/BackgroundServices/TableFeedSensorsService.cs` | Posting, registration, paths, descriptions |
| `src/server/HSMServer/ServerConfiguration/Sections/TableSensorsConfig.cs` | The settings |

## Tests

`src/tests/HSMServer.Core.Tests/TableSensors/TableFeedSensorsTests.cs` (the tick is called with its time, the counters' clock is a seam): the counters of a stored block (rows per table, product rows, bytes equal to the stored length, refused tables) and a duplicate that adds nothing; a block with every table refused; the lag as the largest of the period and a negative one; the sensors as created (paths, types, units, descriptions, values, in the product's tree under `.tables`, not table sensors, the table sensor untouched); an idle product and table posting nothing, the 0 once after a failure period and silence after it, the table period; the per-table sensors switched off; a removed table sensor going silent; a table path with spaces, dots, non-ASCII letters and `.tables` in it, a table path too deep for a feed path, a table sensor on the product's `Rows` path; the keyless value path (missing sensor, table sensor, other type, unknown product); the validation of the settings. `TableMergeJobTests.A_damaged_block_is_kept_listed_and_counted_and_the_rest_of_its_hour_is_merged` asserts the unreadable block reaches the feed counters at the commit.

## Known Issues / Limitations

- The hosted service's own scheduling (`BaseDelayedBackgroundService`) is not covered by a test; its tick is.
- A tick that fails for a product loses that product's counts of the period (logged as an Error); the next period counts again.
- Lag is per product only; a per-table lag would need a time per table in the directory.
