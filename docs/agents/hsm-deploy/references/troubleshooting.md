# Troubleshooting the remote compose stack

Known failure modes of the HSM compose stack (source: `aicontext/architecture/docker.md`,
`wiki-git/Installation.md`). Read this before retrying anything — several wrong-looking
states are normal, and one common failure is made worse by restarting.

Shared by the `hsm-deploy-dev` and `hsm-deploy-live` skills.

**File set for every command below.** Any compose command that operates the stack
must carry the machine's full `-f` list: `docker-compose.yml`, plus
`docker-compose.override.yml` when present, plus `docker-compose.version.yml` when
a pin exists. A bare `docker compose up -d` skips the last two — on a machine with
an override it drops the custom mounts, and on a pinned machine it re-resolves
`app` to `latest` and **recreates `hsm-server`**, silently undoing the pin (and,
mid database migration, restarting it). Build the list once and reuse it:

```bash
cd <dir> && F="-f docker-compose.yml"; [ -f docker-compose.override.yml ] && F="$F -f docker-compose.override.yml"; [ -f docker-compose.version.yml ] && F="$F -f docker-compose.version.yml"; docker compose $F up -d
```

## `docker compose up` fails: "dependency failed to start: container hsm-server is unhealthy"

The app healthcheck gives the database load ~20 min (Docker 25+) / ~25 min (older
daemon) before declaring failure, and Caddy is created only after the app is healthy.
The most common real cause on a long-lived install: a **legacy `SensorValues_*`
database migration** running inside the container. It rewrites the whole history
before Kestrel starts listening, and it keeps running even after compose gave up.

Recovery: wait, do not restart.

```bash
ssh -p <port> <ssh> 'docker logs hsm-server --tail 20'   # watch for "Now listening"
ssh -p <port> <ssh> 'docker inspect hsm-server --format "{{.State.Health.Status}}"'
```

When logs show `Now listening` and health turns `healthy` (next probe can be up to
5 min away), run `up -d` again with the machine's full `-f` file set (see the top
of this file) — it then only creates Caddy.
**Restarting `hsm-server` during the migration starts the migration over.** Re-running
`up -d` while it is still migrating fails the same way; that is expected, not a new
error.

For a database that legitimately needs longer than the budget, raise `start_period`
inside the full `healthcheck:` block in the remote `docker-compose.yml` (keep the whole
block — a trimmed override on an image without its own healthcheck leaves no check at
all, and `depends_on` then fails instantly).

## `hsm-server` stays `starting` for many minutes

`starting` means the probe inside the start period has not passed yet: normal while
LevelDB loads (seconds for a current-format tree, longer for a big one; the probe
interval is 5 min, so a healthy container can sit in `starting` up to that long).
`unhealthy` past ~20–25 min is the signal to investigate logs.

## Web UI / API answers 502, collectors fail

Caddy is the only public entry (`app` publishes no ports). During an app restart
Caddy waits up to 30 s per request (`lb_try_duration`), so a brief 502 window right
after `up -d` can happen; persistent 502 means the app is down:

```bash
ssh -p <port> <ssh> 'docker ps --format "{{.Names}}\t{{.Status}}" | grep hsm-'
ssh -p <port> <ssh> 'docker logs hsm-caddy --tail 30'
ssh -p <port> <ssh> 'docker logs hsm-server --tail 30'
```

## curl from the Windows workstation fails on port 443

Observed on the dev workstation: Git Bash's `curl.exe` (Windows build, schannel TLS)
fails the handshake against Caddy on port 443 — `schannel: failed to receive handshake`,
HTTP code `000` — while port 44330 of the same Caddy answers `200`. Treat a
workstation-side `000` as a client-side quirk first: verify from the machine itself
(the skills' verify commands already run there over ssh) or from a browser before
investigating the server.

## Certificate problems after switching modes

There is no automatic fallback between certificate modes — a failed Let's Encrypt
issuance leaves HTTPS without a certificate rather than silently downgrading. Check
`docker logs hsm-caddy`; fix DNS/reachability (HTTP-01) or the provider token (DNS-01),
then `up -d` with the machine's full `-f` file set to recreate Caddy. Mode is
switched by editing `.env` (user's file — never edit it yourself) followed by the
same `up -d`.

## Old `.env` after a compose file upgrade

New compose features default off when `.env` predates them — an upgrade then silently
runs without them. Compare **key names only** (never values — `.env` holds tokens):

- `COMPOSE_PROFILES` missing/without `logs` → VictoriaLogs stack does not run.
- `HSM_STRUCTURED_LOGS` missing → app writes no JSON log, so vlagent has nothing to ship.
- `VL_UI_USER`/`VL_UI_PASSWORD` unset → log UI/API not exposed (that is the shipped
  default; both must be set together, password ≥ 12 chars, to enable).

Ask the user to add the missing keys themselves; `up -d` with the machine's full
`-f` file set applies.

## Caddy / VictoriaLogs image never updates

`hsmonitoring/hsm-caddy` and the VictoriaLogs images are version-pinned **in the
compose file itself**; `docker compose pull` alone never moves them. Updating the
remote `docker-compose.yml` from `origin/master` (the compose-file sync step of the
update flow) is how pinned versions advance. Versioned tags are immutable — if the
pulled caddy tag does not exist yet, the master CI workflow that publishes it has not
finished; wait for it.

## Rollback to the previous app version

The previous image stays on the host until pruned. Find it and re-pin:

```bash
ssh -p <port> <ssh> 'docker images --format "{{.ID}}\t{{.Repository}}:{{.Tag}}\t{{.CreatedSince}}" | grep hierarchical_sensor_monitoring'
```

Then on the remote, create (or edit) `docker-compose.version.yml` with
`services: { app: { image: 'hsmonitoring/hierarchical_sensor_monitoring:<older-tag-or-digest>' } }`
and run `up -d` with the full `-f` list — base, `docker-compose.override.yml` when
present, then the pin:
```bash
ssh -p <port> <ssh> 'cd <dir> && F="-f docker-compose.yml"; [ -f docker-compose.override.yml ] && F="$F -f docker-compose.override.yml"; F="$F -f docker-compose.version.yml"; docker compose $F up -d app'
```
(If the old version only survives as an image ID, `docker tag <id> hsmonitoring/rollback:<date>`
first and pin that.) Rollback is a decision for the user — offer it, do not decide it.

## Disk pressure

Images plus the VictoriaLogs named volume share the host filesystem. Check with
`df -h <dir>`; the log store usage specifically:

```bash
ssh -p <port> <ssh> 'docker run --rm --volumes-from hsm-victorialogs alpine du -sh /victoria-logs-data'
```

The store self-caps at `VL_RETENTION_MAX_DISK` (default 10 GiB, oldest days dropped
first); the app's own JSON archives under `Logs/Archives/` (up to 60 daily files) are
outside that cap and count toward `Logs/`.

## `docker compose pull` fails (rate limit / network)

Docker Hub anonymous pull limits or a flaky link — retry later or have the user
`docker login` on the remote. Never "fix" this by switching image sources.
