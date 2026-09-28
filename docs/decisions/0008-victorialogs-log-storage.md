# ADR-0008: VictoriaLogs for HSM server log storage in the docker-compose deployment

**Status:** Accepted
**Date:** 2026-09-25
**Supersedes:** —

---

## Context

Operators and AI agents need to search HSMServer logs: by time, level, logger, and message text. Today the logs are pipe-separated text files under `Logs/`, which means downloading files and grepping by hand. The supported Docker Compose deployment (ADR-0007) is the environment to improve; Windows bare-metal deployments are out of scope for v1. The server logs through NLog with a sink-level credential redaction wrapper (`${hsm-redacted}`, `hsm_pat_` tokens) that must survive any new pipeline unchanged.

A design session (#1470) walked the full decision space: store, shipper, format, read path, auth, retention, and enablement.

## Decision

Add an infra-only `logs` profile to docker-compose.yml (issue #1470), with no .NET code changes (one NuGet package reference adds the NLog WebService target):

- **Store: VictoriaLogs** (upstream `victoriametrics/victoria-logs`, version-pinned, single container). Chosen over alternatives below. Retention via `-retentionPeriod` (`VL_RETENTION_PERIOD`, default 30d, minimum 1d), 512 MB cgroup memory limit, data in a named volume. No hard disk quota exists in VictoriaLogs; disk usage must be monitored externally (documented `du` one-liner in `aicontext/architecture/docker.md`).
- **Format at source: additive single-line JSON NLog targets** in `src/server/HSMServer/nlog.config` (fields: `_time`, level, logger, thread, traceId, `_msg` incl. exception; `${hsm-redacted}` preserved inside the JSON attributes). The rule is gated on `HSM_STRUCTURED_LOGS=true`, set only by the compose `app` service, so other deployments are unaffected. `JsonLayout` escapes newlines: one event = one line. The field names are VictoriaLogs' reserved ones (`_time`, `_msg`), so the same line is both the archive format and the ingest body. `_time` is UTC round-trip ISO 8601 (`${date:universalTime=true:format=o}`): an explicit `Z` designator keeps it correct even if the app container's `TZ` is later changed.
- **Ingestion: direct from NLog, no shipper sidecar.** The `vl-web` target (WebService target from the `NLog.Targets.WebService` package, JsonPost protocol, a single nameless parameter carrying the same `JsonLayout` as the raw request body) POSTs each event to VictoriaLogs' `/insert/jsonline` endpoint on the compose network — ingest is never exposed outside it. The URL is hardcoded to the compose-internal name; the env-gated rule never fires in other deployments, so the target is inert there. A `RetryingWrapper` (3 retries) covers blips and the targets-level `async="true"` isolates the app. The `jsonfile` target stays as the **durable archive** and backfill source: a prolonged VictoriaLogs outage leaves a gap in the store (best-effort by design), and closing it afterwards is one curl — the archive file is exactly what `/insert/jsonline` ingests (verified live against the pinned image: a 201-line file → 201 events, event times preserved; `application/json`, which JsonPost sends, is accepted).
- **Read path: through the existing Caddy** on the web ports, same origin and TLS as the UI. `/select/vmui` (built-in web UI) and `/select/logsql/*` (query API) route to VictoriaLogs behind Caddy `basic_auth`; credentials (`VL_UI_USER`/`VL_UI_PASSWORD`) live in `.env`, and the existing caddy entrypoint bcrypt-hashes the password so the plaintext never reaches the Caddyfile or the adapted configuration. In the pinned VictoriaLogs version the built-in UI is served under `/select/vmui` (the `/vlui` path of older releases does not exist). Nothing else is proxied: `/insert/...` is compose-network-only. The routes disappear entirely when the credentials are unset. **Default-off:** `.env.example` ships the `VL_UI_*` pair commented out, so a fresh install stores logs but exposes nothing until the operator opts in; the entrypoint additionally refuses the published `change-me` placeholder (case-insensitive) and passwords shorter than 12 characters. The Caddyfile/entrypoint change ships as a new pinned hsm-caddy image tag (2.11.4-2) per ADR-0007.
- **Enablement:** VictoriaLogs runs under `profile: logs`; `.env.example` ships `COMPOSE_PROFILES=logs` so storage is on by default and disabled by removing one line. The public read routes are a separate, deliberate opt-in via the `VL_UI_*` pair.

## Consequences

- Operators and agents get time-, level-, logger-, and message-scoped search over server logs with no new external dependencies beyond one upstream image and one small NuGet package.
- Log flow starts only when an app image containing the new nlog.config and the `NLog.Targets.WebService` package is released; the compose and Caddy pieces can be deployed independently and simply see no events until then.
- **Best-effort delivery residual (accepted):** a prolonged VictoriaLogs outage leaves a gap in the store — events are dropped after three retries plus the async queue overflow, because NLog has no durable remote buffer. The archive file keeps everything, and the documented backfill is one curl; this trade buys having no shipper sidecar to run, monitor, and upgrade.
- The basic-auth credential is **separate from HSM users/roles**: access equals knowledge of the password. Rotating it means editing `.env` and `docker compose up -d` (the hash is regenerated per start).
- **bcrypt cost 10 residual (accepted):** the entrypoint hashes at cost 10 instead of Caddy's default 14. This Caddy also terminates TLS for sensor ingestion, and every *failed* basic-auth attempt pays the full bcrypt cost (only successful checks are cached), so cost 14 would let unauthenticated traffic with random `Authorization` headers burn Caddy CPU and degrade ingestion — a denial-of-service lever on the public listeners. Cost 10 is tens of milliseconds per verification; the compensating control is the enforced 12+ character random password. Unauthenticated traffic still burns *some* CPU per request — accepted; revisit with rate limiting or an IP allowlist only if it is seen in practice.
- Same-origin residual: the VictoriaLogs UI is served from the same origin as the HSM web UI, so an XSS in the VictoriaLogs UI could ride an HSM session cookie (HttpOnly prevents reading it, not reusing it). If this ever matters, moving the UI to a subdomain is a one-line Caddyfile change.
- No disk quota on the log volume: unbounded growth is possible if log volume rises or retention is raised; monitoring guidance is documented.
- Redaction is inherited, not duplicated: the JSON target wraps message and exception in the same `${hsm-redacted}` wrapper as the text targets, so `hsm_pat_` tokens are redacted before the line ever reaches disk.

## Alternatives Considered

- **Loki + LogCLI:** rejected because a usable UI needs Grafana, and Grafana integration is deprecated in HSM (2026-09-14); shipping a deprecated stack for log search was not acceptable.
- **ELK / OpenSearch:** rejected as overweight for a single-node compose deployment (multi-service JVM stack, memory footprint, operational surface).
- **Quickwit:** rejected on UI grounds — its search UI story did not meet the operator-facing goal.
- **Seq:** rejected on licensing — the free tier's limits and license terms do not fit an open-source server product's bundled deployment.
- **No shipper (app pushes directly to VictoriaLogs):** chosen after all — see the decision above. The v1 implementation first landed a **Vector** sidecar (file tail → remap → HTTP sink), and the round-1 review surfaced two problems with it: the documented two-file install never fetched `vector/vector.toml` (Docker turned the missing bind source into a root-owned directory and the shipper restart-looped), and the wiring depended on four upstream mechanics found only by trial (no native `victoria_logs` sink in Vector 0.58, `/insert/jsonl` renamed to `/insert/jsonline`, framing defaults sending JSON arrays, no default CMD). NLog's WebService target turned out to cover the same need with zero extra containers, volumes, or config files — JsonPost with a single nameless JsonLayout parameter sends exactly the NDJSON line `/insert/jsonline` expects — so the sidecar was dropped in review rather than patched around. The on-disk files remain the source of truth; the store is a searchable, best-effort derivative.

## Phase 2

Agent access without the shared basic-auth credential is planned as MCP log-query tools on the #1391 track (HsmApiToken auth, no second credential). The query API exposed here is the same one those tools will call.

## Related

- #1470 — the issue carrying the full decision table
- ADR-0007 — the bundled Caddy image this builds on (path routing, entrypoint templating, immutable versioned tags)
