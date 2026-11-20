# Sealed Sensor History

HSM keeps sensor history in one LevelDB database per week. A background job, the **sealing job**, can store closed weeks in a compact form: the values of each sensor are encoded into chunks of up to 4,096 values, about 7 to 8 bytes per value instead of about 120 bytes per stored row. Every read (the web UI, the Sensor API, Grafana, exports) returns the same values as before; the only difference is that `ReceivingTime` of a sealed value is kept to the millisecond.

The job is **off by default**. This page is for the operator of an HSM server: what the job does, how to switch it on, how to watch it and what to do when it reports a problem.

---

## What it saves

A test deployment, sealing seven closed weeks one after the other:

| Week | Values sealed | Bytes before → after | Sensors | Seal time |
|---|---|---|---|---|
| 1 (oldest, mostly emptied by retention) | 72 | 7,829 → 1,765 | 8 | 0.2 s |
| 2 | 103 | 10,676 → 4,615 | 34 | 0.1 s |
| 3 | 133 | 14,902 → 3,409 | 17 | 0.1 s |
| 4 | 95 | 10,080 → 2,836 | 17 | 0.0 s |
| 5 | 375,569 | 47,874,427 → 2,528,898 | 293 | 2.2 s |
| 6 | 499,551 | 63,655,209 → 3,339,107 | 302 | 2.7 s |
| 7 | 523,514 | 66,665,479 → 3,592,023 | 395 | 2.6 s |
| **Total** | **1,399,037** | **178.2 MB → 9.47 MB** | | |

"Before" is the bytes of the stored rows (keys and values), "after" the bytes of the chunks that replace them. A row database on disk is usually larger than its rows until LevelDB compacts it, and smaller than them after.

A production deployment's week of 11.9 million values from 3,990 sensors, sealed with the same codec, became 91.8 MB of chunks (7.7 bytes per value). On a smaller production week (531,000 values, 492 sensors) the week took 21.8 MB on disk as compacted rows, 25.6 MB while the job kept the original rows next to the chunks, and 3.8 MB once they were removed and the week compacted.

The saving shows on disk only after the **keep period** (below): until then a sealed week holds both its rows and its chunks.

---

## What changes on disk

Each week of sensor history is a directory under the database folder (`Databases/` in Docker):

| Directory | Holds |
|---|---|
| `SensorValuesV2_<from>_<to>` | The week's rows, as before. In a sealed week it keeps every value written after the seal (late values, edits), File sensor values (never sealed), the values of sensors the job left as rows, and the original rows until the keep period ends. |
| `SensorValuesSealedV1_<from>_<to>` | New: the week's chunks, a LevelDB database of its own. It appears when the job seals the week. |

`<from>` and `<to>` are the same numbers in both names: the two directories belong together.

Both directories also hold small text files whose names start with `hsm-`. They record where the job is with each week; the job reads them at every pass, and the server reads `hsm-sealed` to decide whether a week is served sealed.

| File | Directory | Meaning |
|---|---|---|
| `hsm-compacted` | rows | The week was compacted and has not changed since (written by the background compaction). |
| `hsm-sealing` | chunks | A seal is running or was interrupted. Removed when the seal is done. |
| `hsm-rows-left` | chunks | The sensors the job left as rows, with the reason. |
| `hsm-sealed` | chunks | **The week is sealed**: the chunks are complete and verified. |
| `hsm-keep-source-rows-until` | chunks | First line: the UTC time until which the original rows are kept. |
| `hsm-source-rows-removing` | chunks | The job has begun to remove the original rows: from now on the week is read from rows and chunks together. |
| `hsm-source-rows-removed` | chunks | The original rows are gone. |
| `hsm-unsealing` | chunks | An unseal is in progress. |
| `hsm-unsealed` | rows | An operator unsealed the week; the job leaves it as rows. |

**Do not create, edit, rename or delete these files**, with two exceptions, both described below: editing the first line of `hsm-keep-source-rows-until` to keep a week's rows longer, and deleting `hsm-unsealed` or `hsm-unsealing` to seal an unsealed week again or to cancel an unseal. When the job reports a week as needing attention, the report says which file to repair. Never delete a `SensorValuesSealedV1_*` directory by hand: once the keep period is over it holds the only copy of the week.

---

## Switching it on

The job is configured in the `HistorySealing` section of `Config/appsettings.json`. The server writes the section with its defaults on its first start, so it is usually already in the file:

```json
"HistorySealing": {
  "Enabled": false,
  "SealAfterDays": 7,
  "KeepSourceRowsWeeks": 4,
  "WorkingHoursStartUtc": "00:00",
  "WorkingHoursEndUtc": "00:00",
  "FoldMinRows": 10000,
  "FoldQuietHours": 24
}
```

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Turns the job on. While it is off the job does nothing and creates nothing; weeks sealed earlier stay readable, and the Unseal button still works. |
| `SealAfterDays` | `7` | A week (Monday 00:00 to Monday 00:00, UTC) is sealed this many days after it ended (1 to 3650). Values that arrive later are still accepted: they are stored as rows next to the chunks. |
| `KeepSourceRowsWeeks` | `4` | After a verified seal, the original rows stay this many weeks (1 to 520) before the job removes them. The time is written into each week when it is sealed; a later change applies only to weeks sealed afterwards. |
| `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `"00:00"`, `"00:00"` | The daily UTC window the job works in, `HH:mm`. Equal values mean all day; `"22:00"` to `"06:00"` wraps past midnight. The Unseal button ignores the window. |
| `FoldMinRows` | `10000` | Values written into a sealed week after its original rows were removed are folded into its chunks once there are this many... |
| `FoldQuietHours` | `24` | ...or once their number has not changed for this many hours. |

The section is read when the server starts: **restart the server** after changing it. A value out of range stops the start with an error that names the key.

**When the first pass runs.** The job runs every 7 minutes. Its first pass comes 15 minutes after the server started, rounded up to the job's 7-minute grid, so between 15 and 22 minutes after the start. The log says when at start:

```
History sealing is on: weeks are sealed 7 days after they close, source rows kept 28 days; first pass at 2026-11-20 10:21 UTC, then every 7 minutes
```

Until that time nothing else about the job is logged, and that is expected. Each pass does at most one thing to one week, so a backlog of closed weeks is sealed one week per pass, oldest first.

---

## Watching it

### The log

| Line | Level | When |
|---|---|---|
| `History sealing is on: ...; first pass at ... UTC, then every 7 minutes` | Info | At start, when the job is on (`History sealing is off` otherwise). |
| `History sealing: Seal Done SensorValuesV2_...; 523514 rows sealed, 66665479 -> 3592023 bytes; 395 sensors sealed in 2.6 s; seal commit held the gate up to 2.1 ms` | Info | Every pass that did something: the action, the outcome, the week, what it sealed or removed, how long it took and how long writes to the week had to wait. |
| `History sealing: ... Failed ...` | Error | A pass whose action failed. The week waits a day; the other weeks go on. |
| `History sealing pass skipped: the history shard compaction step was running (...); next pass at 02:07 UTC` | Info | The pass found another maintenance step running (see below). |
| `History sealing pass: no week needs work` | Debug | A pass with nothing to do. Not shown at the default log level. |
| `Sealing ...: sensor ... stays rows (reason): ...` | Warn | A sensor the job left as rows (see `Sensors left as rows` below). |
| `Sealed history: ... needs attention: ...` | Error | Once per week and reason; see `Weeks needing attention`. |
| `... held the gate of ... for ... ms: writes to the week waited` | Warn | A step of the job made writes to an old week wait more than 100 ms. On the test deployment the longest wait was 9.8 ms. |

**A skipped pass.** The job shares one maintenance slot with the background compaction of history weeks (every 10 minutes), the Compact button and the Unseal button: only one of them runs at a time. A pass that finds the slot taken does nothing and logs the line above with what held it. The next pass is 7 minutes later, so the gap between two passes is then 14 minutes. That is by design and needs no action.

### Self-monitoring sensors

Under **HSM Server Monitoring → Database → Sealed history**, posted with the server's self-monitoring every 5 minutes, one value per pass:

| Sensor | What it shows |
|---|---|
| `Last run` | The action and outcome of the last pass (for example `Seal Done`, or `None NothingDue` when there was nothing to do); the comment is the full report. A new value every 7 minutes means the job is alive. Error status when the pass failed. A skipped pass posts nothing. |
| `Sealed weeks` | Weeks stored as chunks after the last pass. |
| `Weeks needing attention` | Weeks the job will not touch until you act (see below). Error status above 0; the comment names each week and the reason. |
| `Rows sealed`, `Bytes before`, `Bytes after` | What the last seal or fold encoded, and its size before and after (MB). |
| `Sensors left as rows` | Sensors the last seal or fold left as rows, with the reasons in the comment. |
| `Verification failures` | Sensors whose chunks did not read back as their rows. They stay rows; anything above 0 is a defect to report. |
| `Chunk read failures`, `Chunk rewrite failures`, `Chunk directory sync failures` | Counters since the server started. Error status above 0; the log names the chunk or directory. |

**Sensors left as rows** are normal in small numbers: File sensors are never sealed (and not counted), the encoder refuses some values it cannot store exactly (for example a `ReceivingTime` it cannot round to the millisecond), and a sensor whose history was deleted while the job encoded it waits for a later pass. Such sensors are read as before; the job tries again only when their rows change.

### Weeks needing attention

| Reason in the comment | What it means | What to do |
|---|---|---|
| A chunk directory without `hsm-sealed` | The file was lost (removed by hand, a partial restore, file-system damage). The week is now served from its rows alone, which may be nearly empty after the keep period. The job leaves the chunks alone. | Put `hsm-sealed` back into the `SensorValuesSealedV1_*` directory (any content) and restart the server. If you know the week's rows are complete (the keep period had not ended), you may delete the directory instead. |
| `hsm-keep-source-rows-until` cannot be read | The original rows are kept until it is fixed. | Write a UTC time on its first line, for example `2026-12-01T00:00:00.0000000Z`. |
| A chunk cannot be read during source-row removal | The rows of that chunk stay; the rest of the week went on. The removal is retried daily. Nothing is lost. | Clear the history of the sensor named in the chunk read error over that week, or leave it. |
| A chunk cannot be read during an unseal | The unseal stopped before anything irreversible and is retried daily. | Cancel the unseal (delete `hsm-unsealing`), or clear the history of that sensor over that week and press Unseal again. |
| The delete record (watermark) of a sensor cannot be read | Retention and clear history of that sensor in that week are refused, so its old values there stay, and reading that sensor's values in that week fails. | Unseal this week. The unseal restores every value of the sensor's chunks, also values retention had already deleted (the log names the sensor); retention then removes them again. |

---

## The keep period

After a week is sealed and every chunk verified, its original rows stay in `SensorValuesV2_*` for `KeepSourceRowsWeeks` weeks. While they are there, everything the job did can be undone without loss, and the week takes more space than before. Reads of such a week use its rows alone, exactly as before the seal: the chunks are not read, and after a restart their database is not even opened until retention, a compaction or the job needs it. When the period ends, the job writes `hsm-source-rows-removing` (from then on reads combine rows and chunks), removes every original row that still equals its value in the chunks (a value rewritten after the seal stays), writes `hsm-source-rows-removed`, and the background compaction shrinks the row directory once the week has been quiet for 3 hours.

**To keep one week's rows longer**, edit the first line of `SensorValuesSealedV1_<from>_<to>/hsm-keep-source-rows-until` to a later UTC time in the same format (`2027-01-15T00:00:00.0000000Z`). The job reads it at every pass; no restart is needed. Leave the rest of the file as it is.

---

## Unseal

**Configuration → Database**, select the week in *Values Database* and press **Unseal** (administrators only). It works whether the job is on or off.

What it does: every value of the chunks that the row directory does not already hold is copied back into it, the week is switched back to rows while the server runs, `hsm-unsealed` is written into the row directory and the chunk directory is deleted. Values come back with `ReceivingTime` to the millisecond. During the keep period the rows are all still there, so an unseal is quick and changes almost nothing. A week of about 500,000 values unseals in about 1.5 s.

An unsealed week **stays rows** for good: the job does not seal it again until you delete `hsm-unsealed` from its `SensorValuesV2_*` directory.

If a read was still using the chunks, the chunk directory is deleted by a later pass (or by pressing Unseal again), not at once.

**To cancel an unseal** that cannot finish, delete `hsm-unsealing` from the `SensorValuesSealedV1_*` directory. The week returns to the stage it was in; the values already copied are folded away later.

---

## Rolling back to an older server version

A server version without sealed history does not know the `SensorValuesSealedV1_*` directories. It does not delete them; it simply does not see them:

- **During the keep period** every original row is still in `SensorValuesV2_*`, so the older version shows the whole history of sealed weeks.
- **After the keep period** the older version shows only what is still stored as rows: late values, File sensors, sensors left as rows. The rest of those weeks is hidden, not lost. **Unseal those weeks before rolling back.**

An older version also deletes without knowing about chunks: retention and "clear history" run by it remove rows only, and the chunks would bring those values back after you upgrade again. Treat a rollback with sealed weeks in place as a way to keep reading, not as a round trip: unseal first if you may come back.

---

## Backups

**Sensor history is not part of the server's backups.** The automatic backup (`BackupDatabase`) copies the configuration database and the dashboards database only; no weekly history database, sealed or not, is in it. This was so before sealing and has not changed.

To keep a copy of sensor history, copy the database folder yourself, with the server stopped. For a sealed week copy **both directories together**, `SensorValuesV2_<from>_<to>` and `SensorValuesSealedV1_<from>_<to>`, with every file in them: `hsm-sealed` lives inside the chunk directory, so a restored chunk directory is complete only with it, and after the keep period the rows directory alone does not hold the week. Restore them together as well.

---

## What it costs

- **CPU and memory while sealing.** Sealing a week of about 500,000 values takes 2 to 3 seconds of one core and about 25 MB of managed memory (40 to 80 MB of working set) while it runs; for a week of 11.9 million values the encoding alone is about 10 seconds, verification comes on top. The job runs one action every 7 minutes at most.
- **Retention.** Retention on a sealed week costs almost nothing: it deletes the chunks that lie wholly before its cutoff and records, per sensor, up to where the remaining chunk counts as deleted (a few dozen bytes); nothing is re-encoded. The values before the cutoff stay on disk inside that one chunk per sensor until a later retention pass or a fold removes it. (The first test deployment ran a build that re-encoded that chunk at every hourly pass; its week being trimmed took 16 MB for 2.5 MB of chunks.)
- **Rolling back to an earlier build of this feature.** Builds from before that change read the per-sensor retention records as damaged chunks. Unseal first if you have to go back to one.
- **Writes.** New values are never written to a sealed week's chunks; they go to the rows directory as before. Writes to an old week wait while the job commits a step; on the test deployment the longest wait was 9.8 ms.
- **Reads during the keep period: nothing.** The week is read from its rows alone, as if it were not sealed. (The first test deployment ran builds that also read the chunks during the keep period and threw the result away: on a test set of 7 such weeks, reading a page of 100 values of every sensor in every week took 4 times as long and allocated 8 times as much memory as on the same weeks unsealed.)
- **Reads after the keep period.** A read that needs a sealed value decodes one chunk: about 0.2 ms instead of a few microseconds for the first value or a point read. Reading a whole week of a sensor gets faster. Loading a sensor's history at its first use after a start costs about 0.1 to 0.2 ms more per sealed week it reaches.
- **Memory.** A sealed week's chunk database is a second LevelDB database next to the week's rows. Open and idle it costs about 0.2 MB and four file handles; its chunks are read through memory-mapped files, which count as file-backed memory of the process (reclaimable, like the rows' files). During the keep period it is not opened after a restart until retention or the job needs it. A 300-hour test run of hourly retention, late values, page reads and job passes on 7 sealed weeks showed no growth of managed memory.
- **Disk, temporarily.** A sealed week takes more space than before until its keep period ends (rows and chunks), then far less.
