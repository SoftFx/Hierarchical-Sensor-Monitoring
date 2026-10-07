---
name: hsm-deploy-dev
description: Update and deploy the HSM server on the remote DEV machine over SSH using docker compose (app + Caddy + VictoriaLogs stack), with an optional version parameter (default latest, pre-releases allowed). Use whenever the user mentions the dev machine — updating to the latest or a specific version, pinning a pre-release, checking status, or migrating the old load.sh install: "обнови дев", "поставь на dev 3.42.0", "проверь dev", "обновление на дев-машине". If the user names the live machine (лайв, прод, production), that is the hsm-deploy-live skill.
---

# HSM deploy — DEV machine

Operates the HSM compose stack (`hsm-server` behind `hsm-caddy`, plus
`hsm-victorialogs`/`hsm-vlagent` with the `logs` profile) on the single remote **dev**
machine over SSH. Dev is not production: updates run immediately after a preflight
report, no confirmation gate. The promotion path is: update dev here → verify → the
live skill handles the live machine.

Run from the repo root — the flow reads `docker-compose.yml` and `.env.example` from
`origin/master` and references live under `docs/agents/hsm-deploy/references/`.

If the request does not clearly name the dev machine, ask which machine is meant
before doing anything; the sibling skill `hsm-deploy-live` covers the other one.

## Machine config and one-time SSH setup

Connection details are machine-local and never committed. Read
`.agents/skills/hsm-deploy-dev/machine.local.json` (next to this file):

```json
{ "ssh": "user@dev-host", "port": 22, "dir": "/usr/HSM" }
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
- `X.Y.Z` or `server-vX.Y.Z` → **pinned**: deploy exactly that release, including
  pre-releases (which never move the `latest` tag). Verify it exists first, or a typo
  silently pulls nothing:

  ```bash
  gh release view "server-v<X.Y.Z>" --repo SoftFx/Hierarchical-Sensor-Monitoring
  ```

  If the release is marked pre-release, say so in the plan message (allowed on dev).
  The version pins only the app image; Caddy/VictoriaLogs versions come from the
  compose file (synced below), independent of the app version.

## Modes

| Mode | When |
|---|---|
| status | "проверь/что там на деве" — read-only |
| update | "обнови дев [версия]" — default |
| migrate | machine still runs the old `load.sh` container (`HSMServer_*`) — follow `docs/agents/hsm-deploy/references/migrate-to-compose.md`; it preserves all paths and data |

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

Plus `docker logs hsm-server --tail 50` if the user asks about health/errors. For
context show the newest published server releases (the list also contains Linux-probe
releases — filter them out):
`gh release list --repo SoftFx/Hierarchical-Sensor-Monitoring --limit 10 | grep server-v | head -3`.
(The app tag is `latest`, so the running version is the image label, not the tag.)

## Mode: update

1. **Preflight (read-only)** — current state, and the pre-migrate check:

   ```bash
   ssh -p <port> <ssh> 'cd <dir> && docker compose ps && docker inspect hsm-server --format "{{.Image}}" && df -h .'
   ```

   Record the image ID for the before/after check. Stop and report if the compose
   project is missing (→ migrate), `hsm-server` is unhealthy *before* the update, or
   free disk is under a few GiB.

2. **Report the plan, then execute immediately** (no confirmation gate on dev): one
   message with current version/image → target (release name and whether it is a
   pre-release), whether the compose file will change, then proceed. Announce each
   step's result as you go.

3. **Sync `docker-compose.yml` from origin/master.** Caddy and VictoriaLogs image
   versions are pinned *inside* the compose file, so they only advance when the file
   itself is updated. Never sync from a local branch.

   ```bash
   git fetch origin
   git show origin/master:docker-compose.yml | ssh -p <port> <ssh> 'cat > <dir>/.docker-compose.yml.new'
   ssh -p <port> <ssh> 'diff -u <dir>/docker-compose.yml <dir>/.docker-compose.yml.new || true'
   ```

   Stage inside `<dir>`, not in shared `/tmp` under a predictable name — this file
   runs as root containers, and the diff shown must be the file that gets applied.

   If different, apply with a backup:
   ```bash
   ssh -p <port> <ssh> 'cp <dir>/docker-compose.yml <dir>/docker-compose.yml.bak-$(date +%Y%m%d-%H%M%S) && mv <dir>/.docker-compose.yml.new <dir>/docker-compose.yml'
   ```
   - If the remote has a `docker-compose.override.yml` or mounted custom `Caddyfile`,
     do not remove them — warn that compose/Caddyfile changes may need re-merging
     (wiki-git/Installation.md, "Advanced Caddyfile customization").
   - The sync overwrites local edits to the base compose file — an ad-hoc fix like
     a raised `start_period` belongs in `docker-compose.override.yml`, not the base
     (see troubleshooting). If the diff reverts a local edit instead of applying an
     upstream change, say so in the plan message instead of applying silently.
   - **Never modify the remote `.env`** (holds TLS tokens). Do compare its *key names*
     against `origin/master:.env.example` and report keys the user may want to add
     (e.g. `HSM_STRUCTURED_LOGS`, `VL_UI_*`). Extract names only — never `cat` the
     file (that puts tokens into the chat), never values, no
     `docker compose config`:

     ```bash
     ssh -p <port> <ssh> "sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' <dir>/.env | sort"
     git show origin/master:.env.example | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' | sort
     ```

4. **Pull and up** — two variants by target. `up -d` blocks until the app is healthy
   (caddy's `depends_on` waits for it), so during a legacy database migration one
   command can occupy the full budget — run these with a long/background timeout,
   or detached (`nohup … > /tmp/hsm-up.log 2>&1 &`) and rely on the health poll:

   *latest:*
   ```bash
   ssh -p <port> <ssh> 'cd <dir> && rm -f docker-compose.version.yml && docker compose pull && docker compose up -d'
   ```
   (removing a stale pin is what "обнови дев" без версии means: back on `latest`)

   *pinned X.Y.Z:* write the pin, then pull and up with the machine's **full file
   set** — base, the user's `docker-compose.override.yml` when present (any explicit
   `-f` list disables its automatic loading, and that file is where a custom
   Caddyfile mount would live), then the pin:
   ```bash
   printf 'services:\n  app:\n    image: "hsmonitoring/hierarchical_sensor_monitoring:X.Y.Z"\n' | ssh -p <port> <ssh> 'cat > <dir>/docker-compose.version.yml'
   ssh -p <port> <ssh> 'cd <dir> && F="-f docker-compose.yml"; [ -f docker-compose.override.yml ] && F="$F -f docker-compose.override.yml"; F="$F -f docker-compose.version.yml"; docker compose $F pull app && docker compose $F up -d'
   ```

   The same file set applies to every later compose command on a pinned machine —
   a bare `docker compose up -d` skips `docker-compose.version.yml`, re-resolves
   `app` to `latest`, and recreates `hsm-server` (see troubleshooting).

5. **Wait for health.** The app reports `starting` until the LevelDB tree is loaded —
   normally seconds-to-minutes, budget allows ~20–25 min. Poll in short bursts of
   ~5 min each, repeating the call until healthy or the budget is spent (agent shell
   tools kill commands past their own timeouts — one 30-min loop reads as a failure
   while the app is still loading):

   ```bash
   ssh -p <port> <ssh> 'for i in $(seq 1 10); do s=$(docker inspect hsm-server --format "{{.State.Health.Status}}"); echo "$(date +%T) $s"; [ "$s" = healthy ] && exit 0; sleep 30; done; exit 1'
   ```

   On failure read `docs/agents/hsm-deploy/references/troubleshooting.md` before
   retrying — the common causes have fixed recovery paths, and restarting
   `hsm-server` during a legacy database migration restarts the migration.

6. **Verify (read-only)**:

   ```bash
   ssh -p <port> <ssh> 'cd <dir> && docker compose ps'
   ssh -p <port> <ssh> 'docker inspect hsm-server --format "{{.Image}}"'        # must differ from step 1 — unless the plan was app-image-neutral (compose-only sync, re-pinning the running version)
   ssh -p <port> <ssh> 'curl -sk -o /dev/null -w "%{http_code}\n" https://127.0.0.1/api/sensors/testConnection'   # through caddy, expect 200
   ssh -p <port> <ssh> 'curl -sk -o /dev/null -w "%{http_code}\n" https://127.0.0.1:44330/api/sensors/testConnection'
   ssh -p <port> <ssh> 'docker logs hsm-server --since 15m 2>&1 | tail -20'     # expect "Now listening", no errors
   ```

   A 200 through caddy proves caddy is up AND the app is serving (caddy starts only
   after the app healthcheck passes).

7. **Report**: machine, image ID old → new (plus the version label when the image
   carries one), services recreated,
   health and verification results, pin state (`latest` or pinned `X.Y.Z` — note that
   a plain "обнови дев" returns the machine to `latest`; when pinned, also say that
   every later compose command on that machine must repeat the full `-f` file set —
   bare `docker compose` silently reverts the app to `latest`). Suggest checking the web UI
   and one collector; when the goal was release verification, remind that promoting
   to live is the live skill's job.

## Hard safety rules

- Never `docker compose down -v`, `docker volume prune`, or anything that writes into
  `Databases/`, `DatabasesBackups/`, `CaddyData/` — that is sensor history and TLS
  state. Plain `docker compose down` is also unnecessary: `up -d` recreates only
  changed services.
- Never edit, print, or overwrite the remote `.env`; the user changes it themselves.
  The single exception is creating `.env` from the template during the one-time
  migration (see the migrate reference, step 4).
- This skill never touches the live machine, even read-only — that machine may be
  mid-update by the live skill.

## References (shared with hsm-deploy-live, run from repo root)

- `docs/agents/hsm-deploy/references/migrate-to-compose.md` — one-time switch from
  the old `load.sh` container to compose, preserving all paths and data.
- `docs/agents/hsm-deploy/references/troubleshooting.md` — failure modes and recovery.
- `docs/agents/hsm-deploy/references/ssh-key-setup.md` — one-time password → key login.
