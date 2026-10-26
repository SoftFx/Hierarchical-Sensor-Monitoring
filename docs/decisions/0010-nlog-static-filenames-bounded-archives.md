# ADR-0010: Static NLog file names with size-based rotation bound the Logs/ disk

**Status:** Accepted
**Date:** 2026-10-23
**Supersedes:** the archive-budget paragraph of ADR-0008 (Consequences, "Archive-file disk cost")

---

## Context

On 2026-10-23 the dev machine's `Logs/` directory held **49 GiB** and the disk was 100% full (#1583): 566 dated text files accumulated since January, 16 dated JSON files since structured logging was enabled, and a two-day event flood (2026-10-20/21) had added 22 GiB. `VL_RETENTION_MAX_DISK` capped only the VictoriaLogs volume, not the app's own files.

Every `nlog.config` target used a `${shortdate}`-dated `fileName` with `archiveEvery=Day` and `maxArchiveFiles=60`. That combination never rotates anything: NLog archives a file — and applies `maxArchiveFiles` cleanup — **only when rotating a static fileName**. With a dated fileName each day simply opens a new file, and yesterday's file is abandoned in `Logs/` forever. The only limit that ever fired was `allfile`'s 100 MB size rotation (which archives from the open file mid-day); the `jsonfile` target — the structured shipping source — had no size cap at all.

ADR-0008 had explicitly declined `archiveAboveSize` for `jsonfile` on the premise that "the 60-file cap already bounds the total". The incident disproved that premise: the cap only ever saw files rotated from a static name, which the dated targets never produced.

## Decision

All `File` targets in `src/server/HSMServer/nlog.config` and `src/module/HSMPingModule/nlog.config` write a **static fileName** and rotate it **daily and at 100 MB** into `Logs/Archives/` with `archiveNumbering="DateAndSequence"` (unique date+sequence names for the multiple same-day archives a size rotation produces), keeping `maxArchiveFiles="60"` per target — ~6 GiB worst case per target. The cap is per target and the four server targets rotate independently, so the server's `Logs/` worst case is their sum, **~24 GiB** (plus the module's two targets where co-located); disks must be sized for the total, not for one target. The static `jsonfile` name (`HSM-structured-log-current.json`) must keep matching vlagent's `-fileCollector.glob=HSM-structured-log-*.json`; `NlogConfigTests` pins the static names, the rotation limits, and the glob match.

## Consequences

- `Logs/` disk usage is bounded even under event floods; the flood that filled the dev disk would now cap `jsonfile`'s archives at ~6 GiB (the server's whole `Logs/` bounds at ~24 GiB worst case — four independent targets × ~6 GiB).
- `errorfile` retention changes meaning from ~60 days to 60 files: with size rotation added, every 100 MB archived mid-day during an error flood takes one of the 60 slots, so ~6 GiB of flood errors evicts the pre-flood error history — exactly the history needed to investigate the flood. Accepted: normal error volume rotates daily, so months of history are still kept, and giving `errorfile` larger limits would split the single uniform retention contract every target follows.
- Mid-day size rotation during a flood archives file tails into `Logs/Archives/` that vlagent never reads (ADR-0008's rotation edge case, now also mid-day): if vlagent lags, a rotated tail may not reach VictoriaLogs. Accepted — the alternative was unbounded disk. The archive files remain the NDJSON backstop for manual backfill.
- Upgrades from the dated scheme keep the abandoned dated files; each machine needs a one-time manual cleanup (dev: 570 files / 22 GiB removed, #1583). The new rotation only manages files it creates.
- The mixed day+size naming concern that made ADR-0008 decline `archiveAboveSize` is void: `DateAndSequence` numbering exists precisely for day+size rotation.

## Related

- #1583 — the incident and requirements
- ADR-0008 — the log-storage pipeline this retention contract lives inside
