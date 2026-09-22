# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

`docker-compose.yml` runs two services (#1411):

- `app`: the HSM server on **plain HTTP** (`Kestrel__UseHttps: 'false'`). It publishes no ports and is reachable only inside the compose network.
- `caddy` (`caddy:2`): terminates TLS and publishes `80`, `443`, `44330`, `44333`. It obtains and renews the certificate itself. Its Caddyfile is inline in the compose file (`configs.content`, Docker Compose v2.23+), so the whole stack is one file.

The address comes from `HSM_DOMAIN` (`.env` next to the compose file, default `localhost`), which compose interpolates into the Caddyfile:

| `HSM_DOMAIN` | Certificate |
|---|---|
| public DNS name | Let's Encrypt (ACME HTTP-01 on port 80 / TLS-ALPN on 443), auto-renewed |
| IP / `localhost` | Caddy's internal CA (self-signed; clients need allow-untrusted) |

Caddy state (ACME account + certificates) lives in `./CaddyData` and must survive updates, or Let's Encrypt rate limits are hit on re-issue. Admin-facing guide: `wiki-git/Installation.md`.

## Ports

| Port | Purpose |
|---|---|
| 44330 | Sensor API — DataCollector sends values here (`/api/sensors/*`) |
| 44333 | Web UI — browser dashboard, user management, alerts |
| 443 | Web UI on the standard port (compose/Caddy only) |
| 80 | ACME challenge + redirect to HTTPS (compose/Caddy only) |

The public ports equal `SensorPort`/`SitePort` on purpose: the UI/Sensor-API split is by listener port (`Connection.LocalPort`), and the agent bundle falls back to `SensorPort` when no External connection URL is set.

## Volumes

| Volume | Purpose |
|---|---|
| `Logs` | NLog output files |
| `Config` | Server configuration (TLS, Telegram, backup settings) |
| `Databases` | LevelDB data files (sensor history, metadata) |
| `DatabasesBackups` | Automated SFTP backup snapshots |
| `CaddyData` | Caddy certificates and ACME account |

## TLS mode: `Kestrel.UseHttps` (#1411)

`true`: Kestrel serves HTTPS on both ports with `Config/<ServerCertificate.Name>` (PFX, optional `Key` password) or, when that file is absent, the bundled self-signed `default.server.pfx`. The certificate is loaded once at startup, so a renewed PFX needs a restart.

`false`: plain HTTP behind a TLS-terminating proxy:

- both ports listen on plain HTTP/1.1, and no certificate is loaded;
- HSTS and the HTTPS redirect are off, because the proxy owns them;
- `X-Forwarded-Proto` / `X-Forwarded-For` are honoured **only** from `Kestrel.TrustedProxies` (IP or CIDR). An empty list means loopback plus private networks (`10/8`, `172.16/12` including Docker bridges, `192.168/16`, `fc00::/7`). The client's `https` scheme is therefore restored for cookies (`Secure`), for the agent-bundle address (`AgentConnectionResolver`) and for the token-audit IPs, and a direct public client cannot spoof it;
- a malformed `TrustedProxies` entry fails startup with the key named; startup logs a warning that a proxy is expected;
- **the HSM ports must never be published directly**; only the proxy may be reachable.

**Default when the key is absent** (`KestrelConfig.ApplyInstallDefault`; configuration file or the `Kestrel__UseHttps` environment variable both count as set):

- no `Config/appsettings.json` yet (fresh install) → `false`;
- the file exists (existing install) → `true`, written back to the file. An upgrade therefore never takes a server off HTTPS: every collector and agent points at `https://`, and a standalone container has no proxy in front.

Consequences:

- The compose file sets `Kestrel__UseHttps: 'false'`, so an existing install moved to the new compose switches to the proxy, as long as its `appsettings.json` has no explicit `true`.
- Standalone `docker run` paths set `Kestrel__UseHttps=true` explicitly so a fresh standalone container keeps the built-in HTTPS: `docker_scripts/HSMserver/*` and the Playwright container in `.github/workflows/tests.yml`.

## Notes

- No external database service needed — LevelDB is embedded
- Server listens on both ports via Kestrel multi-binding
- TLS is terminated by Caddy in the compose setup; a standalone container serves HTTPS itself with the certificate from the `Config/` volume (see "TLS mode")

## Files To Check

- `docker-compose.yml`
- `docker_scripts/`
- project Dockerfiles
- `nlog.config` / `collector.nlog.config`
- native library paths under `src/lib/`
- app/server configuration classes

## Review Checklist

- Configuration has safe defaults or clear required variables.
- Secrets are not committed.
- Native dependencies exist for supported platforms.
- Startup and shutdown behavior is compatible with long-running services.
- Partial deployment or version mismatch does not silently corrupt data.
- Logs identify config/runtime failures clearly.
