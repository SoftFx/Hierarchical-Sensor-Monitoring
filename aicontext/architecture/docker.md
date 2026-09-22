# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

`docker-compose.yml` runs two services (#1411):

- `app`: the HSM server, unchanged. It serves HTTPS on both ports with its own certificate (see "TLS" below), publishes no ports, and is reachable only inside the compose network.
- `caddy` (`caddy:2`): publishes `80`, `443`, `44330`, `44333` and obtains/renews the certificate clients see. It forwards to `https://app:44333` / `https://app:44330` with `tls_insecure_skip_verify`, because the upstream certificate is HSM's self-signed one. Its Caddyfile is inline in the compose file (`configs.content`, Docker Compose v2.23+), so the whole stack is one file.

`HSM_DOMAIN` (`.env` next to the compose file) is **required**; compose interpolates it into the Caddyfile with `${HSM_DOMAIN:?...}` and refuses to start without it. A default of `localhost` would be reachable remotely: a Caddy site address is a Host/SNI matcher, not a loopback bind.

| `HSM_DOMAIN` | Certificate |
|---|---|
| public DNS name | Let's Encrypt (ACME HTTP-01 on port 80 / TLS-ALPN on 443), auto-renewed |
| IP address | Caddy's internal CA (self-signed; clients need allow-untrusted) |

Caddy state (ACME account + certificates) lives in `./CaddyData` and must survive updates, or Let's Encrypt rate limits are hit on re-issue. The admin-facing guide is `wiki-git/Installation.md`. It embeds this compose file verbatim as the reference setup, and `ReferenceComposeDocTests` fails when the two differ, so change them together. `scripts/local-docker-build.ps1` sets `HSM_DOMAIN=localhost` and publishes Caddy on `127.0.0.1` only.

What the HSM side relies on behind Caddy:

- **UI vs Sensor API split:** decided by listener port (`Connection.LocalPort`); Caddy keeps them apart by forwarding each site to its own upstream port.
- **Request scheme and host:** the upstream hop is HTTPS, so `Request.Scheme` is `https`: no HTTPS redirect, HSTS is still sent, and cookies stay `Secure`. Caddy passes the original `Host` through, so the agent-bundle fallback address (`AgentConnectionResolver`) is the public one with `SensorPort`.
- **Known limitation, client IP:** `RemoteIpAddress` is Caddy's container address; token audit and telemetry record the proxy, not the client. Forwarded headers are deliberately not trusted; doing so needs a trust boundary of its own.

## Ports

| Port | Purpose |
|---|---|
| 44330 | Sensor API — DataCollector sends values here (`/api/sensors/*`) |
| 44333 | Web UI — browser dashboard, user management, alerts |
| 443 | Web UI on the standard port (compose/Caddy only) |
| 80 | ACME challenge + redirect to HTTPS (compose/Caddy only) |

## Volumes

| Volume | Purpose |
|---|---|
| `Logs` | NLog output files |
| `Config` | Server configuration (TLS, Telegram, backup settings) |
| `Databases` | LevelDB data files (sensor history, metadata) |
| `DatabasesBackups` | Automated SFTP backup snapshots |
| `CaddyData` | Caddy certificates and ACME account (compose) |

## TLS

Kestrel always serves HTTPS on both ports with `Config/<ServerCertificate.Name>` (PFX, optional `Key` password) or, when that file is absent, the bundled self-signed `default.server.pfx`. The certificate is loaded once at startup, so a renewed PFX needs a restart. Standalone `docker run` installs present this certificate to clients directly; in the compose setup only Caddy sees it.

## Notes

- No external database service needed — LevelDB is embedded
- Server listens on both ports via Kestrel multi-binding
- Clients get Caddy's certificate in the compose setup; a standalone container serves its own certificate from the `Config/` volume (see "TLS")

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
