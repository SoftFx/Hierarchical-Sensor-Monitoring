---
name: hsm-deploy-live
description: Update and deploy the HSM server on the remote LIVE (production) machine over SSH using docker compose (app + Caddy + VictoriaLogs stack), with an optional version parameter (default latest). Always requires explicit user confirmation before any change and recommends updating dev first. Use whenever the user mentions the live/production machine — "обнови лайв", "выкатить на прод", "обнови live сервер", "поставь релиз на live". Dev machine requests belong to the hsm-deploy-dev skill.
---

# HSM deploy — LIVE machine (production)

Operates the HSM compose stack (`hsm-server` behind `hsm-caddy`, plus
`hsm-victorialogs`/`hsm-vlagent` with the `logs` profile) on the single remote **live**
machine over SSH. This is production: every state-changing step is gated behind an
explicit user confirmation, and dev should have seen the same version first.

Run from the repo root — the flow reads `docker-compose.yml` and `.env.example` from
`origin/master` and references live under `docs/agents/hsm-deploy/references/`.

If the request does not clearly name the live machine, ask which machine is meant
before doing anything; the sibling skill `hsm-deploy-dev` covers the other one.

## Machine config and one-time SSH setup

Connection details are machine-local and never committed. Read
`.agents/skills/hsm-deploy-live/machine.local.json` (next to this file):

```json
{ "ssh": "user@live-host", "port": 22, "dir": "/usr/HSM" }
```

- If missing: copy `machine.example.json` to `machine.local.json`, ask the user for
  the SSH target and deployment directory, fill it in, continue.
- `dir` is the remote directory containing `docker-compose.yml` and `.env`.
- Check non-interactive login once per session before anything else:

  ```bash
  ssh -o BatchMode=yes -p <port> <ssh> 'echo ok'
  ```

  If it asks for a password (fails with "Permission denied (publickey,password)"),
  walk the user through `docs/agents/hsm-deploy/references/ssh-key-setup.md` — a
  one-time key installation; the flow cannot proceed over password prompts.

Run remote commands as `ssh -p <port> <ssh> '<command>'` from Git Bash, quoting the
whole remote command so it runs non-interactively; prefer several small ssh calls over
long inline scripts.

## Version parameter

Parse the target version from the request:

- none / "последний" / "latest" → **latest** (default): the image tag the compose file
  pins, updated by `docker compose pull`.
- `X.Y.Z` or `server-vX.Y.Z` → **pinned**: deploy exactly that release. Verify it
  exists first (`gh release view "server-v<X.Y.Z>" --repo SoftFx/Hierarchical-Sensor-Monitoring`).
  If the release is marked **pre-release**, that is allowed on live ONLY when the user
  named that exact version themselves — never propose one, and surface the pre-release
  status prominently in the confirmation question.
- The version pins only the app image; Caddy/VictoriaLogs versions come from the
  compose file (synced below), independent of the app version.

## Modes

| Mode | When |
|---|---|
| status | "проверь/что там на лайве" — read-only, no gate needed |
| update | "обнови лайв [версия]" — default, gated |
| migrate | machine still runs the old `load.sh` container (`HSMServer_*`) — follow `docs/agents/hsm-deploy/references/migrate-to-compose.md` (it preserves all paths and data), gated like an update |

Detect migrate during preflight: no compose project in `<dir>` **and** no
`hsm-server` container, while either a `HSMServer_*` container exists in
`docker ps -a` (any state — a legacy install sits stopped after a host reboot) or
`<dir>` already holds an `.env` (a migration that placed its files but never
finished the switchover — the old container may or may not still exist) → switch
to the migrate reference instead of updating. A leftover stopped `HSMServer_*`
next to a working compose stack means update, not migrate; deleting the leftover
is the user's call. The reference's step 3 recognizes an interrupted migration
and resumes at step 4.

## Mode: status (read-only)

```bash
ssh -p <port> <ssh> 'cd <dir> && docker compose ps'
ssh -p <port> <ssh> 'docker inspect hsm-server --format "image={{.Image}} health={{.State.Health.Status}}"'
ssh -p <port> <ssh> 'docker inspect hsm-server --format "{{index .Config.Labels \"org.opencontainers.image.version\"}}"'
ssh -p <port> <ssh> 'cd <dir> && cat docker-compose.version.yml 2>/dev/null || echo "no version pin (latest)"'
```

The version-label command prints the version when the image carries
`org.opencontainers.image.version`, and empty when it does not (locally built images
have no labels at all) — in that case the image ID is the version identity.

Plus `docker logs hsm-server --tail 50` if the user asks about health/errors.

## Mode: update — gather everything read-only FIRST, then one gate, then apply

1. **Preflight (read-only)** — current state, and the pre-migrate check:

   ```bash
   ssh -p <port> <ssh> 'cd <dir> && docker compose ps && docker inspect hsm-server --format "{{.Image}}" && df -h .'
   ```

   Record the image ID. Stop and report (do not gate yet — there is nothing to
   confirm) if the compose project is missing (→ migrate), `hsm-server` is unhealthy
   *before* the update, or free disk is under a few GiB.

2. **Compute the compose-file change (read-only, apart from the
   `<dir>/.docker-compose.yml.new` staging file)**:

   ```bash
   git fetch origin
   git show origin/master:docker-compose.yml | ssh -p <port> <ssh> 'cat > <dir>/.docker-compose.yml.new'
   ssh -p <port> <ssh> 'diff -u <dir>/docker-compose.yml <dir>/.docker-compose.yml.new || true'
   ```

   Never sync the compose file from a local branch. Stage inside `<dir>`, not in
   shared `/tmp` under a predictable name — this file runs as root containers, and
   the diff shown at the gate must be the file that gets applied (step 5a re-checks
   it before the swap). The sync also overwrites local edits to the base compose
   file (ad-hoc fixes like a raised `start_period` belong in
   `docker-compose.override.yml`, see troubleshooting) — if the diff reverts a local
   edit rather than applying an upstream change, flag that in the confirmation. If
   the remote has a
   `docker-compose.override.yml` or mounted custom `Caddyfile`, note it in the
   confirmation (custom copies may need re-merging — wiki-git/Installation.md).
   **Never modify the remote `.env`**; compare only its *key names* against
   `origin/master:.env.example` and list keys the user may want to add — extract
   names, never `cat` the file (that puts tokens into the chat), never values:

   ```bash
   ssh -p <port> <ssh> "sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' <dir>/.env | sort"
   git show origin/master:.env.example | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' | sort
   ```

3. **Dev status check (read-only, best effort)**: if
   `.agents/skills/hsm-deploy-dev/machine.local.json` exists, read the dev machine's
   current version and health the same way (image label + `State.Health.Status`).
   Unreachable or unconfigured dev is a note, not a blocker.

4. **THE GATE — explicit confirmation.** Present everything in one interactive
   question (AskUserQuestion or the session's equivalent) and wait:
   - live: current image/version → target (release name; pre-release flagged loudly
     if applicable);
   - compose file: will change / already in sync (one-line diff summary);
   - `.env` key drift, if any (names only);
   - dev: "healthy on 3.41.6" (target reached — the recommended path) or
     "still on 3.41.5 / not reachable" (recommend updating dev first; proceed only if
     the user insists);
   - estimated downtime: normally under a minute (container restart + database load;
     longer if a legacy database migration runs — it self-reports, see troubleshooting).

   No state-changing command runs before an explicit yes. If the user says dev was
   updated earlier in another session, the auto-check in step 3 still decides what the
   confirmation shows. A declined gate cleans up after itself —
   `rm -f <dir>/.docker-compose.yml.new` — so a later session cannot mistake the
   staged file for fresh.

5. **Apply** (only after the gate passes). Order matters — swap the compose file
   first (it carries the pinned Caddy/VictoriaLogs image versions), then pull, then up.
   `up -d` blocks until the app is healthy (caddy's `depends_on` waits for it), so
   during a legacy database migration one command can occupy the full budget — run
   it with a long/background timeout, or detached
   (`nohup … > /tmp/hsm-up.log 2>&1 &`) and rely on the health poll in step 6.

   a. *Compose file* (only if step 2 found a diff): re-run the diff first — the gate
   may have sat unanswered for a long time and the staged file must be exactly what
   was shown there — then swap:
   ```bash
   ssh -p <port> <ssh> 'diff -u <dir>/docker-compose.yml <dir>/.docker-compose.yml.new || true'
   ssh -p <port> <ssh> 'cp <dir>/docker-compose.yml <dir>/docker-compose.yml.bak-$(date +%Y%m%d-%H%M%S) && mv <dir>/.docker-compose.yml.new <dir>/docker-compose.yml'
   ```

   b. *latest:* remove a stale pin, pull, up:
   ```bash
   ssh -p <port> <ssh> 'cd <dir> && rm -f docker-compose.version.yml && docker compose pull && docker compose up -d'
   ```
   (removing a stale pin is what "обнови лайв" без версии means: back on `latest`)

   c. *pinned X.Y.Z:* write the pin, then pull and up with the machine's **full
   file set** — base, the user's `docker-compose.override.yml` when present (any
   explicit `-f` list disables its automatic loading, and that file is where a
   custom Caddyfile mount would live), then the pin:
   ```bash
   printf 'services:\n  app:\n    image: "hsmonitoring/hierarchical_sensor_monitoring:X.Y.Z"\n' | ssh -p <port> <ssh> 'cat > <dir>/docker-compose.version.yml'
   ssh -p <port> <ssh> 'cd <dir> && F="-f docker-compose.yml"; [ -f docker-compose.override.yml ] && F="$F -f docker-compose.override.yml"; F="$F -f docker-compose.version.yml"; docker compose $F pull app && docker compose $F up -d'
   ```

   The same file set applies to every later compose command while the pin exists —
   a bare `docker compose up -d` skips `docker-compose.version.yml`, re-resolves
   `app` to `latest`, and recreates `hsm-server` (see troubleshooting).

6. **Wait for health.** `starting` while LevelDB loads is normal (seconds-to-minutes;
   budget ~20–25 min). Poll in short bursts of ~5 min each, repeating the call until
   healthy or the budget is spent (agent shell tools kill commands past their own
   timeouts — one 30-min loop reads as a failure while the app is still loading):

   ```bash
   ssh -p <port> <ssh> 'for i in $(seq 1 10); do s=$(docker inspect hsm-server --format "{{.State.Health.Status}}"); echo "$(date +%T) $s"; [ "$s" = healthy ] && exit 0; sleep 30; done; exit 1'
   ```

   On failure read `docs/agents/hsm-deploy/references/troubleshooting.md` before
   touching anything — restarting `hsm-server` during a legacy database migration
   restarts the migration, and "dependency failed to start" often just needs waiting
   and a second `up -d` (with the machine's full `-f` file set — a bare
   `docker compose up -d` on a pinned machine re-resolves `app` to `latest` and
   restarts the migration).

7. **Verify (read-only)**:

   ```bash
   ssh -p <port> <ssh> 'cd <dir> && docker compose ps'
   ssh -p <port> <ssh> 'docker inspect hsm-server --format "{{.Image}}"'        # must differ from step 1 — unless the plan was app-image-neutral (compose-only sync, re-pinning the running version)
   ssh -p <port> <ssh> 'curl -sk -o /dev/null -w "%{http_code}\n" https://127.0.0.1/api/sensors/testConnection'   # through caddy, expect 200
   ssh -p <port> <ssh> 'curl -sk -o /dev/null -w "%{http_code}\n" https://127.0.0.1:44330/api/sensors/testConnection'
   ssh -p <port> <ssh> 'docker logs hsm-server --since 15m 2>&1 | tail -20'     # expect "Now listening", no errors
   ```

   A 200 through caddy proves caddy is up AND the app is serving. If collectors or
   the web UI were reported broken before, ask the user to confirm one of each.

8. **Report**: machine, image ID old → new (plus the version label when the image
   carries one), services recreated,
   health and verification results, pin state (when pinned, say that every later
   compose command on that machine must repeat the full `-f` file set — bare
   `docker compose` silently reverts the app to `latest`), `.env` key-drift
   follow-ups for the user. Keep an eye on the machine for the next few minutes if
   a legacy database migration was in progress (`docker logs hsm-server`).

## Hard safety rules

- **The gate is absolute**: no state-changing remote command (compose file
  replacement, `pull`+`up`, container stop/rm, migration) without an explicit yes in
  this conversation for exactly this change. Read-only inspection is always allowed.
- Never `docker compose down -v`, `docker volume prune`, or anything that writes into
  `Databases/`, `DatabasesBackups/`, `CaddyData/` — that is sensor history and TLS
  state. Plain `docker compose down` is also unnecessary: `up -d` recreates only
  changed services.
- Never edit, print, or overwrite the remote `.env`; the user changes it themselves.
  The single exception is creating `.env` from the template during the one-time
  migration (see the migrate reference, step 4).
- Never propose a pre-release for live; deploy one only if the user named the exact
  version.
- This skill never touches the dev machine beyond the read-only status check in
  step 3.

## References (shared with hsm-deploy-dev, run from repo root)

- `docs/agents/hsm-deploy/references/migrate-to-compose.md` — one-time switch from
  the old `load.sh` container to compose, preserving all paths and data (includes the
  equivalence table against load.sh).
- `docs/agents/hsm-deploy/references/troubleshooting.md` — failure modes and recovery.
- `docs/agents/hsm-deploy/references/ssh-key-setup.md` — one-time password → key login.
