# HSM Linux probe (`hsm-linux-probe`)

A systemd-hosted Linux host probe that reports into an existing HSM server by **hosting the shared
native collector** (`src/native/collector`) through its stable C ABI.

Architecture and rationale: [`docs/initiatives/linux-docker-probe.md`](../../docs/initiatives/linux-docker-probe.md)
(epic #1413). This directory is workstream 2 (#1415) — the skeleton.

## The one rule that shapes everything here

**The probe implements no sensor the collector already has.** Metric acquisition lives in the
shared collector, conformance-locked against the managed one (root `CLAUDE.md` rules #9/#10); a
probe-local reimplementation would be a permanent semantic/wire divergence.

**Two sets, pinned separately.** The probe registers exactly the sensor set the managed
HSMDataCollector registers on Linux (`UnixSensorsCollection.AddAllDefaultSensors`: computer set +
module set — the [parity contract](#parity-contract)), **plus** a set of
[probe-only sensors](#probe-only-sensors) agreed one by one with the owner. Probe-only sensors
exist only here — never in the shared collector catalog, never on Windows — and go through the
collector's public sensor API, so wire format, queuing, batching, retry and TLS stay the library's;
only the acquisition (a sysfs read, a `statvfs`, a Docker Engine API call) and the schedule live in
the probe. The archive/backup part of #1417 is the next probe-only source.

**The tree sits directly under the product, with no computer node** (owner decisions #1493 and
#1496 — one product = one host). The product root holds the host's `.computer/…` and the probe's
module node `.probe/`, which carries `.module/…` and `Docker/…` — the .NET layout with an empty
`ComputerName`:

```
<product>/
├── .computer/…                      host sensors (CPU, RAM, temperature, disks)
└── .probe/                          the probe (module node)
    ├── .module/…                    Service alive, Collector version/errors, Version, process, queue stats
    └── Docker/<project>/<service>/…
```

`hsm.computerName` is empty by default and `hsm.module` defaults to `.probe`; the skeleton and the
server's install bundle write neither, so the defaults apply. Both keys are still accepted, but a
`computerName` re-introduces a `<computer>/` level and another `module` renames `.probe` — **not
recommended**. This holds for the Linux probe only: the Windows agent (HsmAgent) keeps its
`<MACHINE>/HSM Agent/.module` layout.

The process node name is fixed as `.module/Process process`, the same as HsmAgent, so alert
templates apply across hosts — do not rename it. As in `src/agent`, the process sensors are
registered one by one and `Process ThreadPool thread count` is omitted (a native process has no
CLR pool); the probe posts `.module/Version` itself since the module group helper is not used.

## Parity contract

The registered set is pinned by `probe::tests::the_registered_set_is_exactly_the_managed_unix_default_set`
(the exact path list; the computer set is asserted when built with `linux-default-sensors`).

**How this table was produced.** Both collectors ran for 330 s in Linux containers against the same
fake Sensor API, and every `/commands` registration and `/list` value was captured and diffed:
the managed collector from `src/collector` via `collector.Unix.AddAllDefaultSensors(new Version(0,1,0))`
on .NET 8; the probe via `probe::tests::parity_capture` (an `#[ignore]`d audit test — rerun it with
`HSM_PARITY_ADDRESS`/`HSM_PARITY_PORT` set) linked against the #1414 collector (`4889f3f`,
collector 0.7.0) with `linux-default-sensors` on — the build the garage-server trial ran.

**Registration is byte-identical** for all 15 shared paths: `SensorType`, `OriginalUnit`,
`DisplayUnit`, `TTLs`, `KeepHistory`, `SelfDestroy`, `Statistics`, `AggregateData`, `EnableGrafana`,
`IsSingletonSensor`, `DefaultAlertsOptions`, `EnumOptions`, `Alerts` and `TtlAlerts` all match.
(`Description` is outside the byte contract by design — the managed text interpolates machine data.)
The registration carries no bar period, post period or tick; those columns are what the captured
values show. Every divergence is in value delivery.

Paths are relative to the product root: `.computer/…` there, `.module/…` under the probe's
module node `.probe/` (#1493, #1496).
TTL is "none" for every sensor except `Service alive`, which carries the inactivity alert.

| Sensor | Type · unit | Alerts / KeepHistory (both sides) | Bars / posting: managed → native | Live value: managed / native | Status |
|---|---|---|---|---|---|
| `.computer/Total CPU` | DoubleBar · % | EMA mean > 50 | 5-min bar, partial post every ~15 s (3 samples / 15 s) → 15-s bars (4 samples each), posted every ~20 s | yes / yes | **Known gap #1428** |
| `.computer/Free RAM memory` | DoubleBar · MB | EMA mean < 2 | as Total CPU | yes / yes | **Known gap #1428** |
| `.computer/Disks monitoring/Free space on disk` | Double · MB | EMA value ≤ 20480 → Error | every 5 min → every 5 min | yes / yes (values agree) | Parity |
| `.computer/Disks monitoring/Free space on disk prediction` | TimeSpan | — | every 5 min → — | yes (`00:00:00`) / **no** | **Known gap #1426** |
| `.module/Process process/Process CPU` | DoubleBar · % | — | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process process/Process memory` | DoubleBar · MB | EMA mean > 30720 | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process process/Process thread count` | DoubleBar | EMA mean > 2000 | as Total CPU | yes / yes | Intentional path (#1429); **#1428** |
| `.module/Process <name>/ThreadPool thread count` | DoubleBar | EMA mean > 2000 | managed only | yes / not registered | **Intentional** — no CLR pool in a native process |
| `.module/Service alive` | Bool | TTL inactivity alert → Error · 180 d | every ~15 s → every ~15 s | first `False`, then `True` / `True` from the first post | **Finding F3** |
| `.module/Collector version` | Version | 5 y | on Start and Stop → on Start | `3.5.0.0`, `Start:`/`Stop: dd/MM/yyyy HH:mm:ss` / `0.7.0`, `Start: <ISO-8601>` only | Value: intentional (independent collector version); **F4** comment/Stop; **F1** |
| `.module/Collector errors` | String | — | on error → on error | none / none (clean run) | Parity |
| `.module/Collector queue stats/Items count in package` | IntBar · count | — | 5-min bar, partial post every ~15 s → one post per 5-min bar, at its close | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Package content size` | DoubleBar · KB (MB until #1459) | — | as Items count | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Package process time` | DoubleBar · s | — | as Items count | yes / only after 5 min | **Finding F2** |
| `.module/Collector queue stats/Queue overflow` | IntBar · count | — | on overflow → on overflow | none / none (no overflow) | Parity |
| `.module/Version` | Version | 5 y | on Start and Stop → on Start and Stop (probe-posted) | `0.1.0`, `Start:`/`Stop: dd/MM/yyyy HH:mm:ss` / same format; the Stop post is lost to **F1** | Probe matches managed; **F1** |

**Findings that need collector work** (not fixable in the probe):

- **F1 — High.** The stop drain never delivers on a real transport. `StopWorker()` calls
  `http_transport_->Cancel()` to unblock a hung send; the flag stays set, so the `DrainQueueOnStop()`
  that follows sends through a cancelled transport ("Operation was aborted by an application
  callback") and drops everything: the stop-flushed partial bars, last-value snapshots and anything
  posted just before Stop (e.g. the `Version`/`Collector version` `Stop:` values). Observed on a
  healthy server: `Collector stop dropped 6 pending value(s)`. Every restart loses data (rule #8), and
  the #1107 "flush partial bar on Stop" contract does not hold over HTTP. The conformance corpus
  cannot see it: it runs on the in-memory sender. Present on master too.
- **F2 — Medium, #1428 family.** Non-metric bars (queue stats) keep the 5-min window but get no
  periodic partial post, so they surface only once per 5 min; the managed collector posts the
  running partial every ~15 s. Different symptom from the metric bars in #1428 (which shortened the
  window instead) — the #1428 fix should cover both.
- **F3 — Low.** `Service alive`: the managed `CollectorAlive` posts `False` on its first tick as a
  start marker, then `True`; the native heartbeat posts `True` from the first post.
- **F4 — Low.** `Collector version` comment: the native collector writes `Start: <ISO-8601>` and no
  `Stop:` value; managed writes `Start:`/`Stop: dd/MM/yyyy HH:mm:ss`. (The version *value* differs by
  design: the native collector reports its own independent version.)

## Probe-only sensors

Sensors that exist **only in this probe** (owner decisions of 2026-09-24/29, #1476, #1481, #1416).
They are pinned separately from the parity set by
`probe::tests::the_registered_set_is_the_parity_set_plus_the_probe_only_set` (`PROBE_ONLY_SET` +
`DISKS_GARAGE_SET` + `DOCKER_GARAGE_SET`, nothing more, nothing less, the disks and Docker sets
built from garage-server captures), and their registration shape and alerts by
`probe::tests::probe_only_sensors_register_their_agreed_shape_and_alerts` and
`docker::tests::prime_registers_the_whole_tree_before_start_with_alerts`.

*Legend for the tables below:* **warning** = a notification with the ⚠ icon and **no status
change**; **Error** = a notification that also sets the sensor to Error. **TTL** = three periods
for the 5-minute sensors (48 h, one missed day, for the daily cores value): a source that stops
producing — a read that keeps failing, a thread stuck on a hung filesystem, an unmounted disk —
turns the sensor to Timeout on the server instead of leaving its last value looking fresh.

### Host (#1476)

Computer-level (`is_computer_sensor`), so under `.computer/…` at the product root.

| Path | Type · unit | Period | TTL | Alerts (registered with the sensor) | Source | Records/day |
|---|---|---|---|---|---|---|
| `.computer/Logical cores` | Int | at start + every 24 h | 48 h | — | `sysconf(_SC_NPROCESSORS_ONLN)` (what `nproc` shows) | ≈ 2 |
| `.computer/CPU temperature` | DoubleBar · °C (no `Unit` code exists; said in the description) | a sample every 5 s into a 5-min bar (60 samples) | 15 min | Mean in (80, 90] → warning; Mean > 90 → **Error** | rule below | 288 |

**Alert semantics.** Every alert notifies (the managed "instant hourly" schedule: the first
notification at once, repeats hourly while it holds — the same action as the managed default
alerts). HSM alerts can only raise a sensor to **Error**; the server offers no "set Warning" action
and neither does the managed alert DSL. So a *warning* is the notification with the ⚠ icon and no
status change — exactly like the managed Total CPU / Free RAM alerts — and an *error* also sets the
sensor to Error. The warning bands stop where the error band starts, so a single reading never
sends both.

**CPU temperature source rule** (deterministic, first match wins; `probe_only/host.rs`): hwmon
`coretemp` `temp<N>_input` whose label is exactly `Package id 0` → `/sys/class/thermal` zone of type
`x86_pkg_temp` → the first thermal zone whose type contains `cpu`. Directories are visited in
numeric order. Millidegrees → °C. Only the `name`/`type`/`label` attributes of other devices are read,
never their inputs (a `drivetemp` input would wake a sleeping disk). With no source the sensor is
**not registered** (one INFO line). A failed read skips the sample — never a 0 °C — and is logged
once until it recovers.

### Disks — every mounted real filesystem (#1481)

Four sensors per filesystem, named like the Windows per-drive sensors (`Free space on C disk`,
`Average disk write speed on C disk` in the managed and native Windows collectors) with a name in
place of the drive letter: **`root`** for `/`, else the **last segment** of the mount path; names
that collide use the whole mount path with `/` → `_` (`/srv/data` → `_srv_data`), plus `_2`, `_3`
if even that is taken. A name belongs to a mount point and, once given, is kept — across restarts
too: the mount point → name map is persisted in `$STATE_DIRECTORY/disk-names.json`
(`/var/lib/hsm-linux-probe`), so a mount that appears later never renames one that already has
history. Because the name follows the mount point, a different device mounted at a known point (a
swapped USB stick) continues that point's sensors, and a filesystem moved to another mount point
reports under that point's name. All under `.computer/Disks monitoring/`:

| Sensor | Type · unit | Period | TTL | Alerts | Source |
|---|---|---|---|---|---|
| `Free space on <name> disk` | Double · MB (whole MB), EMA | every 5 min | 15 min | — (a fixed 20 GB threshold, as on Windows, would hold a small `/boot/efi` in Error; the % sensor carries the alerts, and `/` keeps the 20 GB alert on the parity `Free space on disk`) | `statvfs` `f_bavail × f_frsize` |
| `Free space on <name> disk %` | Double · Percents | every 5 min | 15 min | [5, 10) → warning; < 5 → **Error** | `statvfs` `f_bavail / f_blocks` (what `df` shows a non-root user) |
| `Free inodes on <name> disk %` | Double · Percents | every 5 min | 15 min | < 10 → warning | `statvfs` `f_favail / f_files`; not registered when the filesystem has no inode count |
| `Average disk write speed on <name> disk` | DoubleBar · MBytes_sec, EMA | a sample every 5 s into a 5-min bar | 15 min | — (the Windows sensor has none) | `/proc/diskstats` sectors written × 512 of the **whole disk** under the filesystem, MB = 1024² |
| `Written per day on <name> disk` | Double · GB (**decimal**, 10⁹ bytes, three decimals), no statistics | the same 5-s samples; **posted once a day**, in its last 30 s | **26 h** | — (owner decision) | Σ Δ `/proc/diskstats` sectors written × 512 of the whole disk during one **local day** (the host's timezone); registered wherever the write speed is |

**Cost:** ≈ 288 × 4 + 1 ≈ **1 150 records/day per filesystem** (`Written per day`: 1/day);
garage-server has four (`root`, `wd4tb`, `mediacentr`, `oldlinux`) ≈ 4 600/day.

**Written per day — the midnight rules** (#1498; until 0.6.1 this was `Written today`, posted
every 5 minutes — those nodes are stale history since). Each 5-s sample adds the disk's delta to
the current local day; the first sample after local midnight starts the new day from 0. **The day
is posted once**, in its last 30 seconds (six samples, so one slow read or a late tick still lands
in it): the day's total. What is still written between the post and midnight counts towards the
next day — counted once, never lost. The ledger remembers the posted day, so a restart inside that
window does not post it twice, and a day is marked posted only when a value went out (if every post
failed, the next sample in the window tries again). What a marked day leaves unposted is logged with
the filesystem or disk, the day and its total: a value whose post failed while others went out
(ERROR, not retried), and a measured disk no mounted filesystem posts (unmounted in the window,
INFO). A day whose window was missed — the probe not
running at midnight, or running but not sampling (a suspend, unreadable `/proc/diskstats`, every
post in the window failed) — is not posted; one INFO line names it with its measured total (a day
that ended while the probe was down: at its first sample after the start, once), and the 26-hour
TTL shows the missing day as Timeout. The day only
turns forward: a clock stepped back across midnight (or a DST fall-back at local midnight) keeps
counting into the day it came from until the clock reaches the next one. A timezone change
(`timedatectl set-timezone`) applies without a restart. The first sample, a counter
that went backwards and a clock that went backwards only set a baseline; a gap longer than three
samples that crosses midnight cannot be split between the days and is dropped (a gap inside the
day counts in full). Never an invented 0: a day with no measured delta yet is not posted, and a
day whose measurement began after midnight (installed, or the probe not running at midnight) says
from when in the comment (`measured since 09:00 local time …`). The day's total and each disk's
last counter are kept in `$STATE_DIRECTORY/disk-written.json` (saved every 5 minutes, at the
day's post and on stop),
so a restart continues the day and counts what was written meanwhile. The counters restart at
boot, so the file records the boot id (`/proc/sys/kernel/random/boot_id`): after a reboot the
day continues from its saved total, but the writes between the last sample before the reboot and
the first after it are not counted. Kernel names are not stable (a reboot can swap `sda` and
`sdb`), so each disk's day also records which physical disk it belongs to — its WWID or serial
from sysfs, else the mount points on it: a different disk under a known name starts its day
afresh (with the "measured since" comment), and after a reboot a day is kept only when that
identity matches. The mount-point fallback counts only filesystems mounted now: an unmounted one
keeps its old disk name, and its mount point must not tie a new disk to the old one (#1489).
Disks not seen for more than a day are dropped from the file. One case cannot
be told apart: a removable disk without a WWID or serial swapped, within one boot, for another
such disk at the same mount point — both identities are that mount point, so the second continues
the first one's day. Two filesystems
on one disk (`mediacentr`, `oldlinux` on `sdb`) both report that disk's total, and their
descriptions say so.

**Which filesystems** (`probe_only/disks/mounts.rs`): the block-backed types in
`/proc/self/mountinfo` — ext2/3/4, xfs, btrfs, vfat, exfat, ntfs3, fuseblk, f2fs; everything else
(tmpfs, overlay, squashfs, proc, sysfs, cgroup, devtmpfs, autofs, nfs, cifs, …) is skipped.
Mounts of one source device — bind mounts, sub-directory mounts, the read-only re-mounts systemd
adds in the service's namespace — are **one** filesystem (a mount hidden under a later mount on the
same path is ignored: `statvfs` only reaches the one on top), reported at a whole-filesystem mount
(`root` field `/`) when there is one, then the shortest mount point: on garage-server
`/mnt/.rw/wd4tb`, `/mnt/wd4tb` and `/mnt/wd4tb/backup` (all `/dev/sda2`) are one `wd4tb`. Once
reported, a mount point is kept while its device stays mounted there — another mount of the same
device appearing elsewhere (a backup script mounting it at a shorter path) does not move it. The set is
resolved before the collector starts and **re-scanned every 10 minutes**: a new filesystem
registers its sensors while the collector runs (collector ≥ 0.9.1 re-posts the registration,
alerts included), one that goes away stops reporting (its sensors time out) and resumes under the
same name when it comes back. Before every 5-minute sample the mount table is read again and only
filesystems mounted at that moment are `statvfs`'d — an unmounted mount point would answer for the
filesystem underneath it. **Automounted filesystems** (an `autofs` trigger on or above the path —
`x-systemd.automount` or an indirect map) are never reported: `statfs(2)` follows automount points, so polling one
would remount it after its idle unmount and wake the disk. Names and sensors are never removed, so
on a host that auto-mounts removable media under per-volume paths (`/media/<user>/<LABEL>`,
`/run/media/…`) every new stick leaves sensors behind in Timeout — add those paths to
`probe.disks.exclude` there.

**Known gap — a separate `/home` (or `/root`).** The unit's `ProtectHome=yes` makes `/home`,
`/root` and `/run/user` inaccessible in the service's namespace: systemd first **unmounts**
everything there and then over-mounts an inaccessible node (verified on garage-server with
`systemd-run -p InaccessiblePaths=/run/lock`: the original mount is gone from the unit's
mountinfo). So a real filesystem mounted there does not exist for the probe and is not reported.
The probe names it with one WARN line when `/etc/fstab` lists a real filesystem at or below such
a path (a filesystem mounted by other means — a mount unit, a script — cannot be seen at all). On such a host, `ProtectHome=read-only`
in a drop-in makes it visible (read-only; the probe never reads anything under a mount) — a
hardening trade-off left to the operator. garage-server has no separate `/home`.

**Which disk the write speed is read from** (`probe_only/disks/diskstats.rs`): the mount's
`major:minor` in `/sys/dev/block/` (falling back to the source's name in `/sys/class/block/`); a
partition is replaced by its parent disk (`sda2` → `sda`). Two filesystems on one disk
(`mediacentr` on `sdb1` and `oldlinux` on `sdb5`) both carry that disk's number, and the
description says so. The first sample, a counter that went backwards and an implausible interval
(under 2.5 s or over 15 s) only set the baseline — never a 0.

**What is read — and what never is.** `/proc/self/mountinfo`, `statvfs(2)` of each reported mount
point, sysfs `uevent` files and `/proc/diskstats`. Nothing under a mount is ever opened, listed or
read. `statvfs` is answered from the superblock: **measured on garage-server (2026-09-29)**, it did
not wake the sleeping archive disks — `smartctl -n standby` reported both in STANDBY before, 5 s
after and 35 s after `statvfs` on the FUSE-NTFS `/mnt/wd4tb` and `/mnt/mediacentr` and the ext4
`/mnt/oldlinux`. So the archives are polled directly every 5 minutes; the earlier plan of reading
their free space from a backup-snapshot file is dropped.

**Moved in 0.4.0:** the two #1476 sensors `.computer/Disks monitoring/Free space on disk %` and
`… /Free inodes %` are now `Free space on root disk %` and `Free inodes on root disk %`. Nothing
posts to the old paths any more, and since they were registered with a 15-minute TTL they turn to
**Timeout** 15 minutes after the upgrade — expected, not a regression: **remove those two sensors
on the server** after upgrading from 0.2.x/0.3.x. The managed-parity `Disks monitoring/Free space on
disk` and its `prediction` (the .NET Unix set, `statvfs("/")`) are untouched.

**Isolation.** Each source runs on its own thread; free space and write speed are two sources.
Every `statvfs` runs on a helper thread with a 5 s deadline, and a filesystem whose previous
`statvfs` is still blocked is not asked again, so a hung FUSE daemon parks one thread and costs
only that filesystem's samples (logged once; its sensors time out). Every sample runs under
`catch_unwind`, and nothing is posted before the collector starts. On stop the sources get 2 s;
stuck ones are named, the collector drains anyway, and the process exits without joining a thread
still blocked in a read.

**Configuration** — `probe.hostSensors { enabled, cpuTemperature }` for the host sensors
(`enabled` switches off both; `Logical cores` has no switch of its own) and
`probe.disks { enabled, exclude, writeSpeed }` for the disks: `exclude` is a list of mount-point
patterns (`*` = any run of characters) matched against the mount point a filesystem is named
after, `writeSpeed: false` drops the write-speed sensors. All default on. Before 0.4.0 the host
switches covered the disk sensor, so **while `probe.disks.enabled` is not set** a
`hostSensors.enabled: false` or the deprecated `hostSensors.disk: false` still turns the disks off
— an upgrade never switches them back on; an explicit `probe.disks.enabled` always wins.

### Docker Compose services (#1416)

Everything lives under one node in the probe's module node:
`.probe/Docker/<project>/<service>/<sensor>`, e.g. `.probe/Docker/gitea/db/Service status`. Source: the Docker Engine API on
`probe.docker.socket` (default `/var/run/docker.sock`); every number below is in
`hsm-linux-probe/src/probe_only/docker/contract.rs`, every alert in `…/docker/alerts.rs`.

| Sensor | Type · unit | Cadence | Value | Alert (at registration) | Records/day |
|---|---|---|---|---|---|
| `CPU` | DoubleBar · % | sample every 5 s (`probe.docker.samplePeriodSec`), 5-min bar | % of the **whole host** (all cores = 100 %): Δ`cpu_usage.total_usage` / Δ`system_cpu_usage` × 100. Not × `online_cpus` — `docker stats` shows per-core % (up to 400 % on 4 cores) | mean > 90 for 30 min → warning notification | 288 |
| `Memory used %` | DoubleBar · % | as CPU | (`usage` − `inactive_file`) / limit × 100; no limit ⇒ of the host's `MemTotal`. **The limit is stated in the description** ("… **1024 MB** on this host", or "no memory limit is set … MemTotal (15917 MB)") and a changed limit re-registers the sensor with the new text — there is no separate limit sensor (owner decision) | mean > 90 → warning notification | 288 |
| `Service status` | Enum (the Windows `ServiceControllerStatus` options) | poll every 60 s, AggregateData | running → Running; created, restarting → StartPending; paused → Paused; exited, dead, removing → Stopped; removed → Stopped for 7 days after last seen. A service first seen as a **completed one-shot job** — every container Exited (0) under restart policy `no` — is not monitored at all (see below) | `IfValue NotEqual Running`, confirmation 5 min, notification repeated hourly — the Windows `ServiceStatusPrototype` alert byte for byte (test-pinned) | ~0 |
| `Health` | Enum {starting, healthy, unhealthy} | poll every 60 s, AggregateData | `State.Health.Status`; registered **only** where a healthcheck exists | `unhealthy` for 5 min → notification, repeated hourly | ~0 |
| `Restart count` | Int · count | poll every 60 s, **posted only on change** | cumulative `RestartCount`, carried across recreates (never goes down) | value changed (`IsChanged`, so a new service's first baseline post does not notify) → notification | ~0 |
| `OOM killed` | Bool | poll every 60 s, AggregateData | `State.OOMKilled`, latched true for 24 h (`probe.docker.oomLatchHours`), across recreates | true → Error + notification | ~0 |
| `Disk written per hour` | Double · MB (decimal, 10⁶ bytes), EMA statistics | one value per clock hour (UTC), **sent just after the hour** | bytes the service's containers wrote to **block devices** in that hour: Δ `blkio_stats.io_service_bytes_recursive` op `write`, summed over devices and replicas, accumulated from the 5-s samples. The value's time is the send time (≈ the hour's end); the comment names the window (`13:00–14:00 UTC`) and, for a partly watched hour, how much was measured. First sample / recreate (new id) / counter reset only set a baseline; an hour with no measurement is skipped, never 0. Page cache counts when flushed; tmpfs never | none (owner decision) | 24 |

Cost: ≈ **604 records/day per service** (two bars, 24 hourly write totals and a handful of state
changes), ≈ 4 830/day for eight services — within the owner's budget. The stats sensors register
only for a service that has run, `Disk written per hour` only once its containers report a write
counter, `Health` only where a healthcheck is defined: no empty nodes.

**Disk written per hour — who wears the disk.** The disks' `Average disk write speed` says how
much a disk is written, not by whom; this sensor splits it by Compose service. Each 5-s sample adds
a container's write-counter delta to the service's current clock hour; the hour is posted on the
first tick after it ends. The running hour and each container's last counter live in the state
file (written at most every 5 minutes, at every posted hour and on stop), so a probe restart
continues the hour — and the writes made while the probe was down count too, when the container
and its counter survived and the hour did not change. A gap that crosses an hour boundary cannot
be split between the two hours and is dropped. The hour that has just ended is posted on the first
tick after it — also when that tick is the first after a restart, since its bytes were measured;
an hour older than that (the probe was not running at the next boundary) is dropped, with an INFO
line. An hour is posted once: a clock stepped back into the hour just posted (an NTP step of a few
seconds across the boundary) does not reopen it — the running hour keeps counting, the writes made
meanwhile included (#1489). A skip that discards measured bytes (a clock that went backwards, a gap
across an hour) is logged once. A write through a stacked device (LVM, dm-crypt, md) is
accounted by the kernel on that device and again on the disk under it; the probe resolves the
stack in `/sys/dev/block/*/slaves` and leaves the stacked device out whenever a disk under it is
listed too. The sensor therefore reports **physical** writes: through LVM or dm-crypt a write
counts once, on the disk; through a mirror (md RAID1/10) **once per member disk**, because each
member really is written (the wear this sensor is for). A container restarted with the same id —
also while the probe was down — is recognised by its new `State.StartedAt` and only sets a
baseline (logged once), since its counter began again from 0. A host that does not account block
I/O per container reports no counter and gets no sensor: Docker Desktop (WSL2) answers an empty
list for every container. On a host that does (some container reports a counter), a running
container that has not written yet — `io.stat` lists a device only after its first I/O — starts
from a zero baseline tied to its start time, so its first write counts in full.

**Registration.** Before the collector starts, the source lists the daemon once and registers
every service it finds (and every service remembered as recently removed), so they ride the Start
registration with their alerts. A service that appears later is registered at runtime: the
collector (0.9.1) posts a sensor created while it runs at its next dispatch cycle, and an alert
attached right after the create call rides that registration.

Behavior at the edges:

- **Identity** is the Compose `(project, service)` from the container labels — stable across
  recreate and upgrade. Segments keep `[A-Za-z0-9_-]`, anything else becomes `_`; two names that
  normalize alike are told apart by a six-hex-digit FNV hash suffix on the newcomer (a name that
  needed no normalization keeps the plain segment). Assigned nodes never move: each service's node
  is remembered in the state file and re-adopted after a restart.
- **Unlabelled containers** (`docker run`): skipped with one log line per container while
  `probe.docker.composeOnly` is `true` (default); with `false` they appear as `Docker/_standalone/<name>`.
  **One-off containers** (`docker compose run`, `com.docker.compose.oneoff=True`) are never counted
  as replicas of their service — a leftover exited one would otherwise pin it at `Stopped`.
- **Excluded services** (`probe.docker.exclude`, owner decision): a service matching a
  `project/service` pattern (`*` matches within one segment, never across `/`) is not monitored —
  no sensors, one INFO line (`<project>/<service>: excluded by probe.docker.exclude, not
  monitored`). A service the state remembers that is now excluded is dropped from the state at
  start (no `Stopped`, no alert). Only an exclude list: whatever is not excluded is monitored.
- **Completed one-shot jobs** (owner decision): a Compose service whose containers have all exited
  with code 0 under restart policy `no` (garage's `lingua-ci/ci-image`, an image build) is a job
  that finished, not a service that stopped — no sensors, one INFO line
  (`<project>/<service>: completed job (exit 0, no restart policy), not monitored`), no config
  knob. Once such a service has a running container it is a service from then on (remembered in
  the state like any other), so its next exit is a real `Stopped`. A non-zero exit, or exit 0 under
  any other restart policy, is `Stopped` as before. An exited container whose inspect failed
  leaves the decision to the next poll.
- **Replicas:** CPU and memory usage are summed (the memory limit sum is capped at the host's
  memory); status and health are worst-of (Stopped < StartPending < Paused < Running; unhealthy <
  starting < healthy); restart counts are summed.
- **CPU is never posted as 0 for lack of data:** a container's first sample, a counter that went
  backwards, a new container id and an interval outside ½…3× the sample period are skipped. A
  service with any skipped replica skips that sample.
- **An unreachable daemon** is logged once (an info line when there is no socket at all, an error
  with a hint on `EACCES`), retried with a backoff doubling up to 60 s, and resumed silently;
  nothing is posted meanwhile. A failed or timed-out inspect or stats call skips that service's
  values (logged once per container) without touching the other services, and never posts a
  guess. A panic in a tick is caught and logged.
- **State** (restart baselines, last posted restart count, OOM latches, last-seen times, nodes,
  the running `Disk written per hour` accumulator) is one JSON file,
  `$STATE_DIRECTORY/docker-state.json` (`/var/lib/hsm-linux-probe`), written atomically only when
  something changed (at most hourly for last-seen; at most every 5 minutes, at each posted hour and
  on stop for the write accumulator). A missing or corrupt file means a fresh start, logged once.

**Engine API client.** A ~250-line HTTP/1.1 `GET` client over `std::os::unix::net::UnixStream`
(`docker/http.rs`) with `Content-Length`, chunked and close-delimited bodies, a 1.5 s deadline per
call covering the connect too (under the 2 s stop wait of the source threads) and a 4 MiB body cap — not the `curl` crate: the socket is local plaintext HTTP, and `curl-sys`
silently compiles its bundled libcurl when pkg-config misses the system one (two libcurls in one
process) and drags `libz-sys`/`openssl-sys` into the link line. The probe still links exactly one
libcurl, the collector's. The client can only build four requests, all `GET`: `/version`,
`/v1.45/containers/json?all=true`, `/v1.45/containers/{id}/json`,
`/v1.45/containers/{id}/stats?stream=false&one-shot=true` (container ids must be hex). The version
is pinned in the path — 1.45, the fixtures' — and negotiated down to a daemon's own (≥ 1.41, for
one-shot stats) or up to its `MinAPIVersion`.

**Socket access.** The socket (`srw-rw---- root:docker`) is root-equivalent; the probe restricts
itself to the four read-only GETs above by construction and review, not by the kernel. The unit
does **not** name the group (`SupplementaryGroups=docker` stops a unit from starting on a host
without that group); the package's postinst runs `/usr/lib/hsm-linux-probe/docker-access.sh
install`, which writes the drop-in `/etc/systemd/system/hsm-linux-probe.service.d/docker.conf` only
when `getent group docker` succeeds (and removes a stale one otherwise) — on a fresh install and on
the upgrade from before 0.3.0 only, so an operator who removed it keeps it removed across upgrades;
postrm removes it on remove/purge. After installing Docker on a host that
already runs the probe: `sudo /usr/lib/hsm-linux-probe/docker-access.sh install && sudo systemctl
daemon-reload && sudo systemctl restart hsm-linux-probe`.

**Tests** run on captures from garage-server (Docker 26.1.5, API 1.45, 12 containers in 6 Compose
projects) under `hsm-linux-probe/fixtures/docker/`: the listing, the inspects (trimmed to the
fields the probe reads — `Config.Env` and mounts dropped), two stats rounds 5.3 s apart, and three
raw HTTP responses (chunked, `Content-Length`, 404) byte for byte.

### Top CPU processes (#1479)

The Windows agents' sensor family on Linux: `.computer/Top CPU processes/<name>`, one Double
sensor per busy process name. **Off unless the top-level `topCpu` block enables it**, exactly like
HsmAgent; the server's Configuration → Agent → "Report top processes by CPU" writes that block into
the downloaded probe bundle as it does for the agent's.

**Placement.** Probe-only by the owner's rule for this epic (new Linux sources live in the probe;
moving one into the shared catalog is a later, separate step). That move would have to mirror a
managed Unix implementation over the same `/proc` source (root `CLAUDE.md` rule #10) with a
conformance scenario (rule #9). It does not break [the one rule](#the-one-rule-that-shapes-everything-here):
the collector's top-CPU source is Windows-only (`hsm_collector_enable_top_cpu_sensors` refuses other
platforms), so on Linux there is no collector sensor to host — only its wire shape to match.

**Indistinguishable from Windows on the wire**, so alert templates written for the agents apply
unchanged — every value is copied from `cpu_top.cpp`/`RunTopCpuLoop` (HsmAgent) and
`WindowsTopCpuMonitor.cs`, which agree:

| | |
|---|---|
| Path | `.computer/Top CPU processes/<name>` (computer sensor: `<ComputerName>/.computer/…` on Windows, the product root here) |
| Type · unit | Double (instant) · `Percents` (100) |
| TTL | 5 min — a name that stops being posted turns to Timeout |
| Options | `EnableGrafana` true; no statistics, no alert, `KeepHistory` left to the server default |
| Description | `Top **10** CPU consumers by % of machine CPU` + a path line (below) |
| Value | % of the **whole host** (all cores = 100 %, like Total CPU), summed over every process of the name |
| Rule | every `periodMs` (1 min): names at or above `minPercent` (1 %), the busiest `count` (10), ties by name; the first period only takes the baseline |
| Name cap | at most `max(count × 8, 64)` distinct names ever get a sensor (the server has no sensor delete); then one WARN line, the tracked names keep reporting |

One registration field differs: Windows sends `DisplayUnit: null`, and an instant sensor created
through the C ABI always sends `0`. The server reads `DisplayUnit` only for Rate sensors.

**Source.** `/proc/stat` (the aggregate `cpu` line without `guest`/`guest_nice`, the collector's
Total CPU total) and, for every numeric entry of `/proc`, `/proc/<pid>/stat`: `utime + stime`
(the whole thread group) over the interval, divided by the host's total over the same interval.
Nothing outside `/proc` is read, and no root is needed. The stat line is split at the **last** `)`
(a process may name itself `a) b (c`). A process is `(pid, starttime)`: a reused pid is never
credited with its predecessor's time; a process that started or exited during the interval
contributes nothing; a `/proc/<pid>` that vanishes while being read is skipped silently. An
unreadable `/proc/stat` is logged once until it recovers and posts nothing.

**Names.** Field 2 of the stat line, which the kernel renders with the same function as
`/proc/<pid>/comm` (one read per process instead of two): `task->comm`, **at most 15 bytes** —
longer executable names arrive truncated (`systemd-journald` → `systemd-journal`). Normalized to
the characters the server's alert-template wildcard `*` matches (ASCII letters and digits, space,
`. _ # , % $ - &`), anything else → `_` (`/`, the path separator, included), then trimmed; an empty
result gets no sensor. **Kernel threads** (`PF_KTHREAD`) are named by the part before their first
`/`, so the per-CPU/per-device instances are one sensor, summed like several `chrome.exe`:
`kworker/3:1-events` → `kworker`, `ksoftirqd/0` → `ksoftirqd`, `irq/42-nvme0q1` → `irq`. Processes
with the same name are summed (Windows rule), also when two names normalize to the same one.

**Path line** of the description, set once when the name's sensor is created, for the name's
busiest process:

* its executable, `readlink /proc/<pid>/exe`;
* for another user's process — most of them: the unprivileged probe may not read that link — its
  **argv[0]** from the world-readable `/proc/<pid>/cmdline`, **only when it is an absolute path**
  (`/usr/local/bin/node`), so a 15-byte `node`/`java`/`python3` still says what it is. Only
  argv[0] is ever used, never the arguments (they can carry secrets): it is cut at the first NUL
  and at the first whitespace, since a process that rewrites its title may join its arguments
  with spaces (the cost: a path containing a space is shown up to it). At most 4 KiB of `cmdline`
  is read;
* `_(another user's process - path unavailable)_` when argv[0] is relative (`python3`), rewritten
  (`postgres: checkpointer`), empty (a zombie) or unreadable;
* `_(system process - path unavailable)_` for a kernel thread (the Windows wording);
* nothing when the process exited first.

Control characters and backticks are removed and a path is capped at 256 characters.

**Visibility — the drop-in.** The unit mounts `/proc` with `ProtectProc=invisible`, which hides
every process but the probe's own. With `topCpu` on, lift it for this unit only:

```ini
# /etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf
[Service]
ProtectProc=default
```

then `sudo systemctl daemon-reload && sudo systemctl restart hsm-linux-probe`. The server bundle's
`install.sh` writes exactly this file when the switch is on (a bundle without it leaves the file
alone); `uninstall.sh` and the package's `postrm` (remove/purge) delete it. Without it — or on any
host that mounts `/proc` with `hidepid` — the source still runs and says so in one INFO line at
start (`/proc is mounted with hidepid=invisible …`); the start log also states how many processes
the baseline saw.

**Cost:** one record per minute per posted name — ≈ 1 440 records/day for each process that stays
at or above 1 % of the host, nothing for the others; at most ≈ 14 400/day with `count` 10. A scan
is one read of `/proc/stat` plus one small read per process per minute.

## Crate layout

```
src/probe-linux/
  hsm-collector-sys/   raw FFI declarations for the ABI subset the probe uses + the CMake build
  hsm-collector/       safe RAII wrapper: Collector, typed sensor handles, alerts, log sink
  hsm-linux-probe/     the binary: config, logging, signals, lifecycle wiring, sensor registration
    src/probe_only/    probe-only sources (host.rs, disks/, docker/, top_cpu/) and their per-source threads
    fixtures/docker/   Engine API captures from garage-server
  packaging/           systemd unit, config skeleton, maintainer scripts, build-deb.sh,
                       docker-access.sh (Docker socket drop-in)
```

**Alerts in the wrapper.** `Collector::alert(AlertKind)` returns an `AlertBuilder` (conditions,
notification / scheduled notification, icon, `sensor_error`, confirmation / inactivity period,
`disabled`, `build`); every sensor handle has `attach_alert(&Alert)`. An alert is part of the
sensor's registration. Before Start it rides the Start batch; while the collector runs (collector
0.9.1) attaching re-records the registration and the live transport re-posts it, which is how a
sensor created at runtime gets its alerts. Attaching is refused only while the collector stops. `instant_hourly_schedule_anchor()` reproduces the managed
`ThenSendInstantHourlyScheduledNotification`. `Collector::enum_sensor_with_options` registers an
enum sensor with both its options and `SensorOptions` (the `Service status` shape; collector 0.9.0).

`hsm-collector-sys/build.rs` configures and builds `src/native/collector` with CMake
(`HSM_COLLECTOR_HTTP=ON`, the same switch `src/agent` uses), links `hsm_collector_core` statically
and libcurl dynamically. `HSM_COLLECTOR_LIB_DIR` short-circuits the build and links a prebuilt
collector instead.

> **TODO(#1413):** once workstream 1 (#1414) ships a `collector-v*` release, the in-tree CMake build
> is replaced by the pinned version from the repo's vcpkg registry — the probe is intended to be the
> first real `x64-linux` consumer of that registry.

## FFI safety rules

The wrapper crate exists to make these mechanical rather than remembered:

* **Nothing unwinds into C.** Every `extern "C"` callback body is wrapped in `catch_unwind`; a
  panicking Rust log sink drops its message instead of crossing the boundary.
* **Handles cannot outlive their collector.** Sensor handles borrow the `Collector`, so the
  use-after-free the raw ABI permits is a compile error here.
* **`Send`/`Sync` follow `hsm_collector.h`, not convenience.** They are asserted only for entry
  points the header documents as callable from any thread; the calls it asks the caller to
  serialize (start/stop/dispose/registration) are serialized by a lock inside `Collector`.
* **Struct layouts are asserted against the real header, at build time.** `build.rs` compiles
  `static_assert`s over `hsm_collector.h` for the size, alignment and every field offset of each
  mirrored struct, and pins the header's version against the version the linked library reports.
  A Rust-only `size_of` test could not do this: it measures the Rust mirror against a constant in
  the same crate, so a field appended on the C side — a MINOR bump under the collector's own
  versioning policy — would change neither and would surface only at runtime, as C writing past the
  end of a Rust stack slot. Verified by deliberately drifting the header: an appended field and a
  reordered field both fail the build.
* **The access key is a `Secret`.** Redacted from `Debug`/`Display`, wiped on drop, never in argv,
  never in an error message, never in the config file. Every copy this code makes — the probe's
  `Secret`, the `CollectorOptions` string, and the `CString` the wrapper hands to the ABI — is
  wiped once the collector has taken it. The collector's own C++ copy is not ours to clear, which
  is why the guarantee is best-effort.

## The #1414 feature gate

`hsm_collector_install_linux_metric_sources` — the entry point that makes the collector's default
host catalog (Total CPU, Free RAM, free disk, process sensors) produce live values on Linux — is
added by workstream 1 (#1414) and **does not exist in master's collector**. So:

* the declaration lives behind the cargo feature `linux-default-sensors`, **off by default**;
* with the feature off the crate builds and runs against today's collector, and the probe logs a
  loud warning at startup naming what will not be reported;
* with the feature off the probe also **skips registering** everything the metric-source factory
  feeds — the `.computer` catalog *and* the three `.module/Process process/…` sensors — rather than
  showing the operator nodes whose values can never arrive. The collector-driven module sensors
  (`Service alive`, `Collector version`, `Collector errors`, the queue stats, `Version`) are
  unaffected and still register;
* with the feature on against an older collector, the link fails — deliberately, so the gap is
  never silent.

Enable it once #1414 is merged: `cargo build --features linux-default-sensors` (and eventually make
it the default here).

## Platform support

**Linux is the only supported target.** OS-specific code is `cfg`-gated so the pure parts (config
parsing, log formatting, the key-permission logic) compile and test anywhere, but the probe is built, tested
and shipped for Linux only; the integration test is `#[cfg(target_os = "linux")]`.

## Building and testing

Requires a C++17 toolchain, CMake ≥ 3.21 and libcurl development headers:

```bash
sudo apt-get install -y build-essential cmake libcurl4-openssl-dev pkg-config
cd src/probe-linux
cargo fmt --all --check
cargo clippy --all-targets -- -D warnings
cargo build
cargo test
```

The CI lane `.github/workflows/probe-linux.yml` runs exactly that on `ubuntu-latest`.

### Building the `.deb`

`packaging/build-deb.sh <version>` builds the package reproducibly inside a plain `debian:13`
container — it installs the build dependencies and rustup itself, builds the release binary with
`--locked --features linux-default-sensors`, checks that the binary loads no shared library outside
the fixed `Depends`, and writes `dist/hsm-linux-probe_<version>_amd64.deb`. From the repository
root on Windows (Git Bash; Docker Desktop):

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W):/src" -w /src/src/probe-linux \
    -v hsm-probe-cargo:/root/.cargo debian:13 bash packaging/build-deb.sh 0.2.0~trial1
```

On Linux drop `MSYS_NO_PATHCONV=1` and use `$(pwd)`. The `hsm-probe-cargo` volume is optional; it
caches the toolchain and crates between runs. The version is a parameter: trials use `~trialN`,
which sorts **below** the plain release (`0.2.0~trial1 < 0.2.0`) and above every `0.1.0~…` —
check with `dpkg --compare-versions 0.2.0~trial1 gt 0.1.0~trial3`.

The package has the layout of the hand-built `0.1.0~trial*` packages: `/usr/bin/hsm-linux-probe`,
`/lib/systemd/system/hsm-linux-probe.service` (kept under `/lib`, where the trials put it — moving a
file between `/lib` and `/usr/lib` across versions is unsafe with dpkg on a merged `/usr`),
`/usr/share/hsm-linux-probe/config.example.json` (the skeleton), `/usr/lib/hsm-linux-probe/docker-access.sh`
and the copyright file; `Depends: libcurl4t64, ca-certificates, libc6, libstdc++6, libgcc-s1`. The maintainer scripts
are `packaging/deb/{postinst,prerm,postrm}`, reconstructed from the trial package: postinst creates
the `hsm-probe` system user/group and reloads systemd, and on a fresh install does not enable or
start the unit (`install.sh` does, once config and key are in place); prerm disables it on remove;
postrm purges `/var/lib` and `/var/log` state. On upgrade prerm stops the unit and, **from 0.2.0 on**,
leaves a marker in `/run` when it was running, so the new postinst starts it again — an upgrade
does not end monitoring. (Upgrading *from* a `0.1.0~trial*` package runs that package's old prerm,
which leaves no marker: start the unit by hand once.) The operator's config is **not** a conffile (from 0.3.1): postinst
seeds `/etc/hsm-linux-probe/config.json` from the skeleton only when it is absent, and no upgrade
touches it or stops at a prompt (a conffile-era file becomes an obsolete conffile: kept, never
prompted about; purge removes it). The upgrade command keeps `--force-confold` as belt and braces:
`sudo apt-get install -y -o Dpkg::Options::=--force-confold ./hsm-linux-probe_….deb`.

## Configuration

`packaging/config.example.json` is the skeleton; it is parsed by a unit test, so it cannot rot.
Placeholders only — **no secrets**:

* `hsm.accessKeyFile` names the file holding the product access key (`CanSendSensorData` only, never
  a master key). A **relative** name (the skeleton's `"access-key"`) is a systemd credential and
  resolves against `$CREDENTIALS_DIRECTORY`, the directory `LoadCredential=` fills — so the config
  never hardcodes `/run/credentials/<unit>/`; without that variable a relative name is an error. An
  absolute path is used as is. The probe reads the key at startup and wipes every copy it
  makes once the collector has taken it (the collector's own C++ copy is not ours to clear).
* `hsm.computerName` (optional, empty by default) and `hsm.module` (optional, `.probe` by
  default): the product root holds `.computer/…` and `.probe/` (#1493, #1496). A `computerName`
  re-introduces a `<computer>/` node, another `module` renames `.probe` — accepted for
  compatibility, **not recommended**. The start log says where the tree sits (`sensors under
  '.probe/'`, with a note when the keys differ from the defaults). **Upgrade note (0.6.1):**
  `module` defaulted to `LinuxProbe` before 0.6.0 and to empty in 0.6.0. A config that leaves
  `module` out therefore moves on upgrade — from `<computer>/LinuxProbe/…` (≤ 0.5.x) or from the
  product root (0.6.0) — to `.probe/…` (the old nodes go stale; `.computer/…` does not move when
  `computerName` is unset). Configs from an older server bundle or the old skeleton set both keys
  explicitly and keep their layout until edited — or until the bundle is re-installed with
  `install.sh --force-config`, which writes the new config and so moves the tree to the default
  layout (the old nodes, their alerts and TTL state stay behind). The server bundle writes that
  layout explicitly (`"computerName": ""`, `"module": ".probe"`; #1495, alongside probe 0.6.3),
  so it does not depend on the defaults of the probe version the server ships.
  **One product per host:** with no host node, two hosts reporting into one product write into the
  same sensors — give every host its own product. For the default layout remove both keys; to keep
  an older one, set `"module"` (and `"computerName"`) explicitly.
* It warns if the key is readable beyond its owner. The check evaluates the POSIX ACL, not just the
  mode bits: systemd hands a credential to a non-root `User=` as a root-owned `0400` file plus a
  named-user ACL entry for the service, which makes `stat` report `0440` (the ACL mask shows in the
  group bits) although no group can read it. That case passes; a genuinely group- or
  world-readable key, or one readable by another named user or group, still warns.
* `hsm.address` must be `https://<host>` (case-insensitive) or a bare host name, which the
  collector defaults to HTTPS. Validation is an allow-list, so `http://`, `ftp://`, `ws://` and a
  typo'd scheme are all rejected rather than passed through to libcurl; the wrapper
  deliberately does not expose the ABI's `allow_untrusted_server_certificate` flag — it disables
  both peer and hostname verification, which §4.1/§4.3 ban. Trust a private CA by installing it
  with `update-ca-certificates`; libcurl/OpenSSL picks up the system store with verification on.
* `probe.docker` (optional; every key has a default): `enabled` (`true`), `socket`
  (`/var/run/docker.sock`), `composeOnly` (`true`), `samplePeriodSec` (`5`, 1–300; the bars stay
  5 minutes whatever it is), `oomLatchHours` (`24`) and `exclude` (`[]`: `project/service`
  patterns, `*` within a segment, e.g. `"portainer/*"`, `"lingua-ci/janitor"`). A host without Docker needs no change: the
  source logs one info line and waits for the socket; `enabled: false` turns it off entirely.
* `topCpu` (optional, **top level** like HsmAgent's, not under `probe`): `enabled` (`false`),
  `periodMs` (`60000`), `minPercent` (`1.0`), `count` (`10`) — the agent's keys, defaults and
  checks (validated only when enabled: `periodMs > 0`, `count > 0`, `minPercent >= 0`). See
  [Top CPU processes](#top-cpu-processes-1479); it also needs the `top-cpu.conf` drop-in.

## Running

```bash
hsm-linux-probe --config /etc/hsm-linux-probe/config.json
hsm-linux-probe --version    # probe version + the linked collector version
```

`SIGTERM` (systemd stop/restart) and `SIGINT` request a graceful stop: the main thread notices
within 200 ms and the collector drains with its own bounded stop, so a host restart is never held
up. An overrun of `shutdownTimeoutSec` is logged; `TimeoutStopSec` in the unit is the backstop.
Values the bounded drain discards are reported at `WARN` (the collector emits that line at
`debug`; the probe raises a non-zero count, since it is data loss — root rule #8).

Log levels are `debug`, `info`, `warn`, `error`. Timestamps and the daily file roll are UTC, like
the collector's own file logger, so the two line up; the journal adds local time on its own.

`packaging/hsm-linux-probe.service` carries the §4.3 hardening (`NoNewPrivileges`,
`ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `StateDirectory`, empty capability bounding
set, `LoadCredential`) and the §5 budget (`MemoryMax=64M`, `CPUQuota=5%`). Docker socket access is
not in the unit but in a drop-in written only where a docker group exists — see
[Socket access](#docker-compose-services-1416).
