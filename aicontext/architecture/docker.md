# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

`docker-compose.yml` runs two services (#1411):

- `app`: the HSM server, unchanged. It serves HTTPS on both ports with its own certificate (see "TLS" below), publishes no ports, and is reachable only inside the compose network.
- `caddy` (`caddy:2.11.4`, pinned): publishes `80`, `443`, `44330`, `44333` and obtains/renews the certificate clients see. It forwards to `https://app:44333` / `https://app:44330` with `tls_insecure_skip_verify`, because the upstream certificate is HSM's self-signed one. Its Caddyfile is inline in the compose file (`configs.content`, Docker Compose v2.23+), so the whole stack is one file.
- Catch-all sites (`https://:443`, `https://:44333`, `https://:44330`) plus `default_sni`/`fallback_sni = HSM_DOMAIN` keep clients that address the host by IP (no SNI) or by another name working. Without them, Caddy refuses the TLS handshake and existing collectors and agent bundles go silent after a migration. Such clients receive the `HSM_DOMAIN` certificate, so they still need allow-untrusted. Verified live, and the per-port UI/Sensor-API split holds for them too. This certificate-selection behavior is why the image is pinned; re-verify IP / no-SNI / foreign-SNI requests when bumping it.
- Caddy writes an access log of every request with its client IP to `./Logs/caddy/access.log` (rolled at 50 MiB, 10 files).

`HSM_DOMAIN` (`.env` next to the compose file) is **required**; compose interpolates it into the Caddyfile with `${HSM_DOMAIN:?...}` and refuses to start without it. A default of `localhost` would be reachable remotely: a Caddy site address is a Host/SNI matcher, not a loopback bind.

| `HSM_DOMAIN` | Certificate |
|---|---|
| public DNS name | Let's Encrypt (ACME HTTP-01 on port 80 / TLS-ALPN on 443), auto-renewed |
| IP address | Caddy's internal CA (self-signed; clients need allow-untrusted) |

Caddy state (ACME account + certificates) lives in `./CaddyData` and must survive updates, or Let's Encrypt rate limits are hit on re-issue. The admin-facing guide is `wiki-git/Installation.md`. It embeds this compose file verbatim as the reference setup, and `ReferenceComposeDocTests` fails when the two differ, so change them together. Server tests do not run on pull requests, so the test catches drift only after merge. It then fails `Build & Test Solution` in `server-build.yml`, the job that also publishes the Docker image, so drift blocks the release image: run it locally when touching either file. `scripts/local-docker-build.ps1` sets `HSM_DOMAIN=localhost` and publishes Caddy on `127.0.0.1` only.

What the HSM side relies on behind Caddy:

- **UI vs Sensor API split:** decided by listener port (`Connection.LocalPort`); Caddy keeps them apart by forwarding each site to its own upstream port.
- **Request scheme and host:** the upstream hop is HTTPS, so `Request.Scheme` is `https`: no HTTPS redirect, HSTS is still sent, and cookies stay `Secure`. Caddy passes the original `Host` through, so the agent-bundle fallback address (`AgentConnectionResolver`) is the public one with `SensorPort`.
- **Client IP (#1427):** HSM restores it from Caddy's `X-Forwarded-For` through `UseForwardedHeaders`, registered first in `ConfigureMiddleware`, only when `Kestrel.TrustedProxies` is non-empty. It uses `XForwardedFor` only (the upstream hop is HTTPS already, so the scheme is never rewritten), `ForwardLimit = 1`, and the framework's implicit loopback trust cleared.
  - The compose file sets `Kestrel__TrustedProxies__0: 'attached-networks'`. At startup HSM expands it to the networks of its own non-loopback interfaces (`KestrelConfig.GetAttachedNetworks`, masked to network addresses) and logs the result. In the container that is the compose network only. `app` publishes no ports, so Caddy is its only possible peer, and the trust boundary is one closed hop. Caddy overwrites incoming `X-Forwarded-For` because no `trusted_proxies` is configured, so a client cannot inject an address.
  - A fixed compose subnet was tried and rejected: Docker hands out `/16`s from `172.17–172.31` to every compose project, so any pinned range collides on a busy host. It did on the development machine: "Pool overlaps with other one on this address space".
  - `TrustedProxies` is `[JsonIgnore]`: deployment-owned, never persisted by `ResaveSettings`. The settings file is added after environment providers, so a persisted copy would shadow a later compose change.
  - Empty (`docker run`, direct access): no forwarded headers are honoured, same as before.
  - With it, the per-source invariant of `ApiTokenInvalidAttemptLimiter`, the token audit source and the key telemetry `RemoteIP` hold behind the proxy as they did without it.
- **TLS client compatibility:** Caddy's TLS 1.2 defaults are AEAD-only, while Kestrel used the platform (OpenSSL) list with CBC suites. Caddy issues ECDSA P-256 certificates by default (Let's Encrypt and its internal CA), and Windows 7 / Server 2008 R2 Schannel does offer `TLS_ECDHE_ECDSA_WITH_AES_*_GCM`, so `net472` collectors on those systems are expected to negotiate. This is not verified on a real Windows 7 client. If one fails, add an explicit `tls { ciphers ... }` to the sites.
- **Recovery when Caddy is down:** `app` publishes nothing, so the documented `docker-compose.recovery.yml` override (`127.0.0.1:44333`) is the way into HSM while the proxy does not start (`wiki-git/Installation.md`, "If Caddy does not start").
- **HTTP on port 80:** Caddy redirects `http://<HSM_DOMAIN>/` to `https://<HSM_DOMAIN>/` (the UI on 443), verified live.

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
