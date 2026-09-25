# ADR-0008: VictoriaLogs for HSM server log storage in the docker-compose deployment

**Status:** Accepted
**Date:** 2026-09-25
**Supersedes:** —

---

## Context

Operators and AI agents need to search HSMServer logs: by time, level, logger, and message text. Today the logs are pipe-separated text files under `Logs/`, which means downloading files and grepping by hand. The supported Docker Compose deployment (ADR-0007) is the environment to improve; Windows bare-metal deployments are out of scope for v1. The server logs through NLog with a sink-level credential redaction wrapper (`${hsm-redacted}`, `hsm_pat_` tokens) that must survive any new pipeline unchanged.

A design session (#1470) walked the full decision space: store, shipper, format, read path, auth, retention, and enablement.

## Decision

Add an infra-only `logs` profile to docker-compose.yml (issue #1470), with zero .NET code changes:

- **Store: VictoriaLogs** (upstream `victoriametrics/victoria-logs`, version-pinned, single container). Chosen over alternatives below. Retention via `-retentionPeriod` (`VL_RETENTION_PERIOD`, default 30d, minimum 1d), 512 MB cgroup memory limit, data in a named volume. No hard disk quota exists in VictoriaLogs; disk usage must be monitored externally (documented `du` one-liner in `aicontext/architecture/docker.md`).
- **Format at source: additive single-line JSON NLog target** in `src/server/HSMServer/nlog.config` (fields: time, level, logger, thread, traceId, msg incl. exception; `${hsm-redacted}` preserved inside the JSON attributes). The rule is gated on `HSM_STRUCTURED_LOGS=true`, set only by the compose `app` service, so other deployments are unaffected. `JsonLayout` escapes newlines: one event = one line.
- **Shipper: Vector** (upstream `timberio/vector`, version-pinned, alpine variant). Tails the JSON log from the shared `Logs/` bind mount with checkpoints, maps NLog field names onto VictoriaLogs' reserved fields (`_time`, `_msg`), and POSTs gzip-compressed newline-delimited JSON to VictoriaLogs' `/insert/jsonline` endpoint **directly on the compose network** — ingest is never exposed outside it. A named volume holds checkpoints and a disk buffer, so lines survive a VictoriaLogs outage and are never re-shipped. Vector 0.58 has no native `victoria_logs` sink; the generic `http` sink with `newline_delimited` framing is the equivalent wiring.
- **Read path: through the existing Caddy** on the web ports, same origin and TLS as the UI. `/select/vmui` (built-in web UI) and `/select/logsql/*` (query API) route to VictoriaLogs behind Caddy `basic_auth`; credentials (`VL_UI_USER`/`VL_UI_PASSWORD`) live in `.env`, and the existing caddy entrypoint bcrypt-hashes the password so the plaintext never reaches the Caddyfile or the adapted configuration. In the pinned VictoriaLogs version the built-in UI is served under `/select/vmui` (the `/vlui` path of older releases does not exist). Nothing else is proxied: `/insert/...` is compose-network-only. The routes disappear entirely when the credentials are unset; the Caddyfile/entrypoint change ships as a new pinned hsm-caddy image tag (2.11.4-2) per ADR-0007.
- **Enablement:** both new services run under `profile: logs`; `.env.example` ships `COMPOSE_PROFILES=logs` so the stack is on by default and disabled by removing one line (plus commenting the `VL_UI_*` pair so Caddy drops the routes).

## Consequences

- Operators and agents get time-, level-, logger-, and message-scoped search over server logs with no new external dependencies beyond two upstream images.
- Log flow starts only when an app image containing the new nlog.config is released (the config ships inside the image); the compose, Vector, and Caddy pieces can be deployed independently and simply see no JSON log until then.
- The basic-auth credential is **separate from HSM users/roles**: access equals knowledge of the password. Rotating it means editing `.env` and `docker compose up -d` (the hash is regenerated per start).
- Same-origin residual: the VictoriaLogs UI is served from the same origin as the HSM web UI, so an XSS in the VictoriaLogs UI could ride an HSM session cookie (HttpOnly prevents reading it, not reusing it). If this ever matters, moving the UI to a subdomain is a one-line Caddyfile change.
- No disk quota on the log volume: unbounded growth is possible if log volume rises or retention is raised; monitoring guidance is documented.
- Redaction is inherited, not duplicated: the JSON target wraps message and exception in the same `${hsm-redacted}` wrapper as the text targets, so `hsm_pat_` tokens are redacted before the line ever reaches disk.

## Alternatives Considered

- **Loki + LogCLI:** rejected because a usable UI needs Grafana, and Grafana integration is deprecated in HSM (2026-09-14); shipping a deprecated stack for log search was not acceptable.
- **ELK / OpenSearch:** rejected as overweight for a single-node compose deployment (multi-service JVM stack, memory footprint, operational surface).
- **Quickwit:** rejected on UI grounds — its search UI story did not meet the operator-facing goal.
- **Seq:** rejected on licensing — the free tier's limits and license terms do not fit an open-source server product's bundled deployment.
- **No shipper (app pushes directly to VictoriaLogs):** rejected — it would require .NET code (out of v1 scope) and couples the server to the log store's availability; the file+shipper chain also keeps the on-disk text logs as the source of truth.

## Phase 2

Agent access without the shared basic-auth credential is planned as MCP log-query tools on the #1391 track (HsmApiToken auth, no second credential). The query API exposed here is the same one those tools will call.

## Related

- #1470 — the issue carrying the full decision table
- ADR-0007 — the bundled Caddy image this builds on (path routing, entrypoint templating, immutable versioned tags)
