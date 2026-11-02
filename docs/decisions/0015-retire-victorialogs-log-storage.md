# ADR-0015: Retire the VictoriaLogs log-storage stack

**Status:** Accepted
**Date:** 2026-11-02
**Supersedes:** ADR-0008 (log storage) and ADR-0012 (the `/logs/` read path)

---

## Context

ADR-0008 added a searchable log store to the compose deployment: VictoriaLogs behind an infra-only `logs` profile, vlagent shipping the app's env-gated JSON log file, and (after ADR-0012) an admin-gated read path at `/logs/` through Caddy `forward_auth` against the app. The pipeline worked as designed, but it is a whole second system around logs: two upstream containers and named volumes, a JSON spool duplicating every Info+ event on disk, retention knobs of its own, and a read path coupled to the app's `/logs-auth` endpoint. The text logs under `Logs/` (`errorfile`/`allfile`/`aspfile`, ADR-0011 rotation) were always the source of truth and already answer the day-to-day questions; the searchable store did not earn its operational surface. Decision (#1594): drop the entire pipeline. Nothing replaces the removed pieces — this is a removal, not a migration to another store.

(Number 0015 was already referenced by a comment in `.env.example` while no such file existed; this ADR takes the number.)

## Decision

- **Compose**: remove the `victorialogs` and `vlagent` services, the `victorialogs-data`/`vlagent-data` named volumes, the `logs` profile, and the `HSM_STRUCTURED_LOGS` environment entry on `app`. The stack is `app` + `caddy` again.
- **`.env.example`**: drop `COMPOSE_PROFILES`, `VL_RETENTION_PERIOD`, `VL_RETENTION_MAX_DISK`, `HSM_STRUCTURED_LOGS`. Leftover values in an existing `.env` are inert.
- **App**: remove the `jsonfile` NLog target and both `HSM_STRUCTURED_LOGS`-gated routing rules from `src/server/HSMServer/nlog.config` (the `Microsoft.AspNetCore*` final rule that keeps framework Info noise out of the text targets stays), and delete the `GET /logs-auth` endpoint (`LogsAuthController`) — its only consumer was the Caddy gate. `NlogConfigTests` now pins the three-target set.
- **Caddy**: drop the `victorialogs-routes` snippet and its imports from the bundled Caddyfile; ships as the new pinned image `hsmonitoring/hsm-caddy:2.11.4-6` under ADR-0007's immutable-tag policy. The entrypoint still exports `HSM_VL_SNIPPET=victorialogs-off`: customized Caddyfiles copied from a pre-2.11.4-4 image must keep loading (their `import {$HSM_VL_SNIPPET}` would otherwise expand to a bare `import` Caddy refuses). Copies merged from 2.11.4-4/-5 carry their own snippet definition and import, still load, and keep refusing `/logs/*`: their `forward_auth` gate against the app's `/logs-auth` runs first, and with that endpoint deleted the gate's non-2xx answer (404, or a login redirect) is passed straight back to the client — 502 only while the app itself is down. Fail closed either way, acceptable until re-merged with the new bundled file.

## Consequences

- Log search over the compose deployment is gone; the text files under `Logs/` and their archives under `Logs/Archives/` are the only log source, read by downloading and grepping (as before ADR-0008).
- **Existing installs (migration):** after upgrading the compose files, stop and remove the two containers (`docker rm -f hsm-victorialogs hsm-vlagent`); both named volumes may be deleted to reclaim disk. Data in `victorialogs-data` is lost by design — the text logs on disk are unaffected. Leftover `.env` keys do nothing.
- The `jsonfile` output already written is orphaned: NLog applies `maxArchiveFiles` cleanup per target on rotation, and the `jsonfile` target no longer exists, so nothing will ever rotate or remove `Logs/HSM-structured-log-current.json` and the `Logs/Archives/HSM-structured-log-*.json` archives (up to 60 × 100 MB ≈ 6 GiB) — the same abandoned-files failure mode ADR-0011 hit with dated names, this time by removal. Until they are deleted, ADR-0011's ~18 GiB worst-case bound does not hold on an upgraded host. Each machine needs a one-time manual cleanup, mirroring ADR-0011's: `rm -f Logs/HSM-structured-log-current.json Logs/Archives/HSM-structured-log-*.json`.
- ADR-0011's retention contract is unchanged for the three remaining text targets; its worst-case `Logs/` bound drops from ~24 GiB to ~18 GiB (three targets × ~6 GiB).
- The ADR-0012 version coupling (a `/logs/`-serving Caddy needs an app with `/logs-auth`) disappears together with both sides.
- `caddy/test.sh` keeps adapting the frozen 2.11.4-3 fixture through the entrypoint: the load-compatibility guarantee for old customized copies is a property of the shipped image, not of the log stack.
- If log search is ever needed again, it is a new decision with new ADRs — not a re-enable: no `logs` profile, snippet, or env gate remains to flip.

## Alternatives Considered

- **Keep the stack, disabled by default:** rejected — the code, images, docs, and compatibility surface (entrypoint variable, forward_auth endpoint, wiki pages) would stay for a feature nobody uses.
- **Replace with another store (Loki, OpenSearch, …):** rejected — ADR-0008's alternative analysis still applies, and the problem statement ("the pipeline does not earn its surface") argues against any store.
- **Keep the JSON spool without a store:** rejected — a second on-disk copy of every Info+ event with nothing reading it, exactly the silent-write behavior ADR-0008's default-off gate existed to prevent.

## References

- #1594 — the removal issue
- ADR-0008 — the stack being retired (basic-auth era, read path superseded by ADR-0012)
- ADR-0011 — the text-log retention contract that remains in force
- ADR-0012 — the `/logs/` read path being retired
