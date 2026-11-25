# Feature: Feed sensors of table sensors

> Owner: server | Last reviewed: 2026-11-25 | Canonical: yes
> Scope: the ordinary sensors the server derives from the table blocks it stores, so that users can set alerts and TTLs on a table feed (#1847, step 5 of epic #1842, ADR-0021 points 14 and 15). The table sensor kind is in `aicontext/features/server/table-sensors/feature.md`, the block store in `aicontext/features/storage/table-blocks/feature.md`.

---

## Overview

A table sensor owns no alerts and no TTL: it has no values to judge. To watch a feed, the server keeps **feed sensors** (glossary): ordinary sensors in the table sensor's own product, under the node `.tables`, posted from what the server already knows when it stores a block (its directory and header, and the store's arrival time) and when the hourly merge commits a product's hour. Users of the product see them in its tree and set alerts on them like on any sensor; the server never sets an alert on them.

| Path (in the table sensor's product) | Type | Unit | Period | Value |
|---|---|---|---|---|
| `.tables/Rows` | Integer | Count | product (1 min) | Rows of the accepted tables of the blocks stored in the period. Rows of refused tables are not counted. |
| `.tables/Stored` | Double | MB | product | Stored length of the blocks stored in the period, as stored (3 decimals). |
| `.tables/Delivery lag` | Double | Seconds | product | The largest, over the blocks stored in the period, of the block's arrival time (as the store recorded it, before its write) minus the time of its last row (the header's `LastTime`). |
| `.tables/Tables refused` | Integer | Count | product | Tables refused in the period, in stored blocks and in blocks every table of which was refused. |
| `.tables/Blocks the merge could not read` | Integer | Count | product | Blocks of the product the merge could not read in the hours it committed in the period (the commit record's `UnreadableBlocks`). |
| `.tables/<the table sensor's path>/Rows` | Integer | Count | table (5 min) | Rows of that table sensor in the blocks stored in the period. |

Each sensor gets a description at its creation that says what it counts, its unit and its period. The feed never changes a sensor that exists: a user's description, unit, alerts, TTL or block stay.

## Invariants

- Counts are per period, not since a start: a restart is not a reset, and the first period after a start is whatever passed between the start and the first tick.
- A duplicate block adds nothing. A block every table of which was refused adds its refusals only (no rows, bytes, lag).
- A block is in the same period for the product's `Rows` and its tables' `Rows`: both counters are drained under one lock.
- **Silence means no feed.** A product or table without activity in a period posts nothing: `Rows`, `Stored` and `Delivery lag` only for a period in which a block of the product was stored; a table's `Rows` only for a period in which a block carried that table (a table present with 0 rows posts 0). A TTL alert on a `Rows` sensor is how a user watches a feed.
- **The failure counters** (`Tables refused`, `Blocks the merge could not read`) post their count in a period in which it is above 0, then **0 once** at the next period without one, then nothing until the next failure. The 0 stays pending until a write of it was accepted: a write that fails, is refused (a blocked sensor, a path left alone) or is dropped is tried again at the next period; a new count in the meantime makes a new 0 due after it.
- **Delivery lag is per product** (the directory has no time per table) and measured against the clock of the provider that wrote the rows, so a clock skew is in it. A negative lag (the provider's clock ahead of the server's) is posted as it is, not clamped. It does not include the time of the server's own synced write.
- A count above the Integer range is posted as `int.MaxValue`; the real number is logged as an Error once per sensor.
- **Occupied paths are left exactly as they are.** A path held by a table sensor, by an ordinary sensor of another type, or by the alias of a moved sensor gets no value and no change from the feed; the server logs it once per path (Warn), not every period.
- **No access key** is created, used or needed by the feed: a product without any key gets its feed sensors too.

## Primary Workflows

1. **Put** (`TableBlockService.Put`): `Match` resolves each table of the block's directory to its table sensor; on the stored outcome the accepted sensors with their directory row counts, the stored length, the refused count, the header's last row time and the index entry's `ArrivalTicks` go to `TableFeedCounters.AddStored`; on the all-refused outcome the refused count goes to `AddRefused`.
2. **Merge commit** (`TableMergeJob.MergeProduct`, after `WriteHour`): the record's unreadable blocks go to `AddUnreadable`.
3. **Tick** (`TableFeedSensorsService`, a hosted service on the product period's grid): every tick drains its period at once (`TableFeedCounters.Drain`, the table counters too when a table period ended; the first tick drains both), builds each product's values with the tick's time, and hands them to that product's **lane**. The tick does not wait for the posts: a tick that finds earlier posts still running drains its own period all the same.
4. **Lanes**: one per product with values to post, alive while it holds any. A lane posts its periods in order through `IServerSensorValues`; lanes of different products do not wait for each other, so a slow product queue delays only its own product, and each period stays its own value. A lane holds at most **60 periods** behind the one being posted (`MaxPendingPeriods`, an hour of the default period); past it the oldest period is dropped, with an Error once per stall (and a dropped 0 stays pending). A failed post is logged as an Error and costs that product's period only.
5. **Removal**: a removed product takes its feed sensors with it (they are its sensors); its counts are dropped at the next tick. A removed table sensor's `Rows` sensor simply goes silent (nothing is counted for it any more) and stays until a user removes it or its self-destroy setting does. A feed sensor a user removed comes back, new, at the next period with activity.

## How a value reaches a user product

The server's own self-monitoring writes into its own product with its own access key. A feed sensor is in a user's product, for which the server has no key, and must not get one. `IServerSensorValues` (`src/server/HSMServer.Core/Cache/IServerSensorValues.cs`) is the narrow way in, with one operation:

```csharp
Task<Dictionary<string, string>> AddServerValuesAsync(Guid productId, IReadOnlyList<ServerSensorValue> values, CancellationToken token = default);
public sealed record ServerSensorValue(SensorValueBase Value, string Description, Unit Unit, string Initiator);
```

- **Not reachable from outside.** It is not a member of `ITreeValuesCache`, which the controllers, the management API and the collector session see. Its implementation, `ServerSensorValues`, is a small non-disposable adapter over the cache's internal `TreeValuesCache.AddServerValuesAsync`, registered in DI by `ApplicationServiceExtensions.AddTableFeedSensors` and given only to `TableFeedSensorsService`. (A non-disposable adapter, so that the container disposes the cache once: an interface registered as a factory returning the cache itself would be tracked and disposed a second time.)
- **One decision per value, on the product's queue, for that exact path in that exact product** (`ServerSensorValuesRequest`):
  - an ordinary sensor of the value's type on the path takes the value; nothing of the sensor is changed;
  - a free path gets its sensor, created with the value's type, the description and unit of the request and the journal initiator `System / Table feed sensors`, and the sensor takes the value. The creation is under the tree's structure lock with the path checked again there, so a sensor or an alias that a move puts on the path in between is found and left alone;
  - a table sensor, a sensor of another type, or an alias (of a sensor moved to another path or product) on the path: refused, nothing changed. An alias is never followed (the moved sensor may be in another product, and its values must not get another product's counts) and never displaced (its collector keeps reaching the moved sensor).
- An added value goes the way of every value: `BaseUpdateRequest.TryCheckRequest`, `AddValueToSensor` (a blocked sensor takes nothing, a rejected value is counted), the history store and the alerts. Not taken (blocked, rejected) is answered per path, so the service knows a 0 was not accepted.
- The answer has a line per value that was not added; an unknown or non-root product is one line keyed by its id.

## Keys and nodes

A sensor's missing parent nodes are created by `AddNonExistingProductsAndGetParentProduct`. For a sensor a collector creates, each new node gets a default access key (`AddProduct` adds `AccessKeyModel.BuildDefault` to a node without one) and the author of the root's first key; a root without keys cannot get new nodes that way. That is existing behaviour of the collector path and is unchanged. For the feed (`keyless`), the new nodes get **no access key and no author** (`AuthorId` empty) and need no key on the root, so a product whose keys were all removed still gets its feed sensors, and the product's key list is what it was.

## Creation

There is no limit on the number of sensors of a product in the server (only an access key's permissions bound what a collector may create); the feed sensors are bounded by 5 per product plus 1 per table sensor. Their history setting is the default (no reason to differ: a feed sensor's values are small and ordinary); a new feed sensor inherits its parent's TTL policies like any new sensor.

A path that is not a valid sensor path is not posted and logged once: a table sensor's path of more than 8 parts leaves no room for `.tables` and `Rows` within the 10 parts a path may have, so such a table has no `Rows` feed sensor; its rows still count in the product's `Rows`.

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
- Memory: the counters of one period; products and tables without activity hold nothing after their period; a lane of a product whose queue does not answer holds at most 60 periods of its values.

## What a user alerts on

- A feed that stopped: a TTL on `.tables/Rows` (product) or `.tables/<table>/Rows` longer than the period.
- A feed that thinned: an alert on `Rows` below an expected count.
- Late data: an alert on `Delivery lag` above a number of seconds (mind the provider's clock).
- A broken registration or schema: an alert on `Tables refused` above 0.
- Damaged storage: an alert on `Blocks the merge could not read` above 0.

## Key Files

| File | What |
|---|---|
| `src/server/HSMServer.Core/TableSensors/TableFeedCounters.cs` | The counters, `TableFeedProductPeriod`, `Drain` |
| `src/server/HSMServer.Core/TableSensors/TableBlockService.cs` | `Feed`; `Put` and `Match` add to it |
| `src/server/HSMServer.Core/TableSensors/TableMergeJob.cs` | `Feed`; the commit adds the unreadable blocks |
| `src/server/HSMServer.Core/Cache/IServerSensorValues.cs` | The interface, `ServerSensorValue`, the `ServerSensorValues` adapter |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.ServerValues.cs` | The operation on the product's queue |
| `src/server/HSMServer.Core/Cache/TreeValuesCache.cs` | Keyless nodes (`AddNonExistingProductsAndGetParentProduct`, `AddProduct`), the sensor's description and unit at creation (`TryAddSensorUnderLock`) |
| `src/server/HSMServer/BackgroundServices/TableFeedSensorsService.cs` | Ticks, lanes, paths, descriptions, the 0-once state |
| `src/server/HSMServer/Extensions/ApplicationServiceExtensions.cs` | `AddTableFeedSensors` |
| `src/server/HSMServer/ServerConfiguration/Sections/TableSensorsConfig.cs` | The settings |

## Tests

- `src/tests/HSMServer.Core.Tests/TableSensors/TableFeedSensorsTests.cs` (a real cache and block store; the tick is called with its time and awaited, the store's clock is a seam): the counters of a stored block and a duplicate; a block with every table refused; the lag from the store's arrival time, the largest of the period, a negative one; the sensors as created (paths, types, units, descriptions, values, in the product's tree, not table sensors) with no access key anywhere and no author on the new nodes; a product without any key; idle silence, the 0 once and the table period; the 0 kept pending while its sensor is blocked; the per-table sensors switched off; a removed table sensor; odd and too deep table paths and a table sensor on a feed path; an existing ordinary sensor that takes the value and keeps its settings, one of another type left alone; a moved feed sensor whose alias is not followed into the other product; a user's alias on a feed path left working; the keyless operation's refusals; the settings' validation.
- `src/tests/HSMServer.Core.Tests/TableSensors/TableFeedSensorsServiceTests.cs` (no database; values go to a recording fake, a slow queue is a gate the test opens, concurrency is a barrier; NLog-isolated collection): a slow product delays only itself and every tick drains its own period; the lane's bound with one Error; a 0 whose write failed or was refused is sent again, for both counters; a clamped count with its Error once; a refused path logged once; concurrent puts and drains lose nothing, count nothing twice and keep a block in one period for both rows; the container disposes the cache once with the feed registered.
- `TableMergeJobTests.A_damaged_block_is_kept_listed_and_counted_and_the_rest_of_its_hour_is_merged` asserts the unreadable block reaches the feed counters at the commit.

## Known Issues / Limitations

- The hosted service's own scheduling (`BaseDelayedBackgroundService`) is not covered by a test; its tick and lanes are.
- Lag is per product only; a per-table lag would need a time per table in the directory.
- Periods a lane holds at a server stop are not posted.
