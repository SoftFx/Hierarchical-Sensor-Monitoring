# Migrate a machine to the compose stack (one-time)

Switches a remote machine from the old practice (`docker_scripts/HSMserver/load.sh` /
plain `docker run`) to the compose stack (app behind Caddy + optional VictoriaLogs),
keeping every byte of data. The trick that makes this safe: the compose file bind-mounts
`./Logs`, `./Config`, `./Databases`, `./DatabasesBackups` **relative to its own
directory**, so dropping `docker-compose.yml` into the existing base directory reuses
the old volumes in place — nothing is copied or moved.

Shared by the `hsm-deploy-dev` and `hsm-deploy-live` skills. Migration is a
state-changing, downtime-carrying operation: confirm the plan with the user first
(downtime is roughly the container restart plus database load, minutes). On the live
machine it additionally goes through that skill's confirmation gate.

## Equivalence with the old load.sh install — verify, then trust

The goal is byte-for-byte the same installation from the app's point of view. Verified
against `docker_scripts/HSMserver/load.sh` and `docker-compose.yml`:

| Aspect | load.sh (`docker run`) | compose | Same? |
|---|---|---|---|
| In-container paths | `/app/Logs`, `/app/Config`, `/app/Databases`, `/app/DatabasesBackups` | identical | ✅ |
| Host paths | `<BaseDirectory>/{Logs,Config,Databases,DatabasesBackups}` (default `/usr/HSM/`) | `./{...}` relative to the compose file — identical **iff the compose file is placed in the same base directory** | ✅ (condition) |
| Container user | `-u 0` (root) | `user: '0'` | ✅ |
| Image | `hsmonitoring/hierarchical_sensor_monitoring` | same | ✅ |
| Host ports 44330 / 44333 | published directly by the app | same numbers, published by caddy and proxied to the same app ports | ✅ numbers; clients now see Caddy's certificate (untrusted-cert flag keeps working) |
| Restart policy | none (down after host reboot) | `unless-stopped` | improvement |
| Container name | `HSMServer_<version>` | `hsm-server` | ⚠ rename: update any host scripts that reference the old name |
| Extra ports | — | 80 and 443 (caddy) | ⚠ new exposure, see step 2 |

Data guarantee: migration only **adds** `docker-compose.yml` and `.env` to the base
directory and swaps the container; it never writes into `Logs/`, `Config/`,
`Databases/`, `DatabasesBackups/`. The LevelDB database is only ever touched by the app
process inside the container — switching the launcher rewrites nothing. (A newer app
image may run its own DB migration on first start, exactly as it would via load.sh —
that is a version update, not a migration effect.)

Enforce the equivalence mechanically: in step 1 record the old container's mount
sources, and after the switch confirm the new container mounts the **same sources**
(see step 6). If the old install's base directory differs from where the compose file
was placed, stop — do not "fix" it by copying data; move the compose file instead.

## 1. Inspect the old install (read-only)

```bash
ssh -p <port> <ssh> 'docker ps -a --format "{{.Names}}\t{{.Image}}\t{{.Status}}" | grep -i hsm'
ssh -p <port> <ssh> 'docker inspect <old-container> --format "{{json .Mounts}}"'
ssh -p <port> <ssh> 'docker inspect <old-container> --format "{{.Image}}"'
```

- The old container is named `HSMServer_<version>` (load.sh convention).
- The mounts reveal the base directory (load.sh default `/usr/HSM`; the source lines
  look like `/usr/HSM/Databases:/app/Databases`). Record the **mount sources** — they
  are the equivalence check for step 6 — and the image ID for rollback.
- If there is no old container at all (fresh machine), just pick a base directory.

## 2. Check port availability BEFORE stopping anything

The compose stack publishes 80 and 443 on the host; if they are taken, Caddy cannot
start after the old container is already down:

```bash
ssh -p <port> <ssh> 'ss -tlnp | grep -E ":(80|443)\b" || echo "ports 80/443 free"'
```

If occupied — stop and report; do not kill the occupying service. Also warn the user
that Docker publishes ports past host firewalls such as ufw: a host that previously
exposed only 44330/44333 becomes reachable on 80/443 (wiki-git/Installation.md,
"Moving an existing installation to Caddy").

## 3. Place the compose files in the base directory

Both downloads are guarded — a machine that already has these files is not a fresh
migrate. An existing `.env` means compose already ran there (its TLS tokens are
irreplaceable): stop and go back to the update flow instead of overwriting.

```bash
ssh -p <port> <ssh> 'cd <dir> && [ ! -e .env ] || { echo ".env already exists — not a fresh migrate, stop"; exit 1; }'
ssh -p <port> <ssh> 'cd <dir> && { [ ! -f docker-compose.yml ] || cp docker-compose.yml docker-compose.yml.bak-$(date +%Y%m%d-%H%M%S); } && curl -fsSL -o docker-compose.yml https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml'
ssh -p <port> <ssh> 'cd <dir> && curl -fsSL -o .env https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/.env.example'
```

The local workstation's checkout may be on any branch — pull the files from
`master` raw, not from the working tree. An unexpected `docker-compose.yml`
(without `.env`) is backed up before the download, same as the update flow.

## 4. Fill `.env` — user's step, not yours

`.env` needs real values before the first start; ask the user and have them either
edit it themselves over SSH or provide the values to paste (then edit without echoing
more than necessary):

- `HSM_DOMAIN` — the address clients use (bare DNS name or IP).
- `HSM_CERTIFICATE` — mode: `letsencrypt-http` / `letsencrypt-dns` (+ `HSM_DNS_PROVIDER`
  and the matching token) / `self-signed` / `custom`.
- Log stack: keep the shipped `COMPOSE_PROFILES=logs` + `HSM_STRUCTURED_LOGS=true`;
  `VL_UI_USER`/`VL_UI_PASSWORD` stay commented out unless the user wants the log UI.

## 5. Switch over

```bash
ssh -p <port> <ssh> 'docker stop <old-container> && docker rm <old-container>'
ssh -p <port> <ssh> 'cd <dir> && docker compose pull && docker compose up -d'
```

Then wait for health and verify exactly as in the update flow's wait-for-health and
verify steps of the invoking skill (first start after migration may legitimately take
longer — see troubleshooting).

Rollback at this point: `cd <dir> && docker compose down`, then bring the old
container back from the **image ID recorded in step 1**. A plain `load.sh <version>`
cannot do that when the old install ran `latest`: `load.sh` defaults to `latest` and
pulls the tag, and step 5's `docker compose pull` has already moved the local
`latest` onto the new image — the old image survives only as an untagged ID. Re-tag
it and load that:

```bash
ssh -p <port> <ssh> 'docker tag <old-image-id> hsmonitoring/hierarchical_sensor_monitoring:rollback && load.sh rollback'
```

(`load.sh` prints a pull error for the local-only `rollback` tag — expected, it
still runs the locally found image. It also runs `docker container prune -f`, which
removes **every** stopped container on the host — say so before running it.) Data
directories were shared, so nothing else needs restoring.

## 6. Post-migration checks

- **Mount equivalence (the data guarantee):** the new container must mount the exact
  sources recorded in step 1 —
  `ssh -p <port> <ssh> 'docker inspect hsm-server --format "{{json .Mounts}}"'`
  — same host directories for `/app/Logs`, `/app/Config`, `/app/Databases`,
  `/app/DatabasesBackups`. A mismatch means the compose file landed in the wrong
  directory: `docker compose down`, move the compose files, `up -d` again — never copy
  or move the data itself.
- Certificate actually issued: from any machine
  `openssl s_client -connect <HSM_DOMAIN>:443 -servername <HSM_DOMAIN> </dev/null 2>/dev/null | openssl x509 -noout -issuer -enddate`
  (self-signed mode shows Caddy's local CA; a failed Let's Encrypt issuance shows
  nothing — then `docker logs hsm-caddy`).
- Collectors keep using `https://<host>:44330` — same port, now terminated by Caddy.
  Clients that allow untrusted certificates keep working in every mode; with a trusted
  certificate they can later drop that flag.
- Web UI: `https://<HSM_DOMAIN>` (or `:44333`).
