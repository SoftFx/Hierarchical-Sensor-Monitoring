# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

`docker-compose.yml` runs two services (#1411):

- `app`: the HSM server, unchanged. It serves HTTPS on both ports with its own certificate (see "TLS" below), publishes no ports, and is reachable only inside the compose network.
- `caddy` (`caddy:2.11.4`, pinned): publishes `80`, `443`, `44330`, `44333` and obtains/renews the certificate clients see. It forwards to `https://app:44333` / `https://app:44330` with `tls_insecure_skip_verify`, because the upstream certificate is HSM's self-signed one. Its Caddyfile is inline in the compose file (`configs.content`, Docker Compose v2.23+), so the whole stack is one file.
- Catch-all sites (`https://:443`, `https://:44333`, `https://:44330`) plus `default_sni`/`fallback_sni = HSM_DOMAIN` keep clients that address the host by IP (no SNI) or by another name working. Without them, Caddy refuses the TLS handshake and existing collectors and agent bundles go silent after a migration. Such clients receive the `HSM_DOMAIN` certificate, so they still need allow-untrusted. Verified live, and the per-port UI/Sensor-API split holds for them too. This certificate-selection behavior is why the image is pinned; re-verify IP / no-SNI / foreign-SNI requests when bumping it.

Two `.env` settings (next to the compose file) are **required**. Compose passes them to the `caddy` container as environment variables (`${VAR:?...}` there, so compose refuses to start without them), and the Caddyfile reads them as `{$VAR}`, written `{$$VAR}` in the compose file. They are deliberately **not** interpolated into `configs.content`: compose does not recreate a container when only an inline config's content changes. Verified: the same container kept the old mode. A changed environment does trigger the recreate, so "edit `.env`, `docker compose up -d`" really switches the mode (verified live: letsencrypt → self-signed → invalid value → self-signed).

- `HSM_DOMAIN`: the address clients use. A default of `localhost` would be reachable remotely: a Caddy site address is a Host/SNI matcher, not a loopback bind.
- `HSM_CERTIFICATE`: the certificate mode (#1431), chosen by the admin; nothing switches automatically.

| Mode | Mechanism | Certificate |
|---|---|---|
| `HSM_CERTIFICATE=letsencrypt` | `import tls-letsencrypt` → `tls { issuer acme }` | Let's Encrypt only (HTTP-01 on 80 / TLS-ALPN on 443). If issuance fails there is no certificate and every handshake fails; a failed renewal keeps serving the still-valid certificate while Caddy retries. |
| `HSM_CERTIFICATE=self-signed` | `import tls-self-signed` → `tls internal` | Caddy's internal CA, issued at start, cannot fail. Clients need allow-untrusted. |
| without Caddy | `docker-compose.direct.yml` (only `app`, ports published) | HSM's own certificate, the pre-#1411 setup. Same data folders, so switching is lossless in both directions. |

A value other than those two fails Caddy's start ("File to import not found: tls-<value>"). **Why no automatic fallback:** #1434 first chained `issuer acme` → `issuer internal`. Issuers are tried in order on every obtain **and renewal**, so a transient Let's Encrypt failure at renewal time (30 days before expiry) could replace a still-valid trusted certificate with an internal one. Browsers that saw the trusted one keep HSTS for 30 days and would then refuse the UI with no click-through. The admin switches deliberately instead; `wiki-git/Installation.md` has the "situation → mode" table and the `openssl` check of the active issuer. Listing `issuer acme` explicitly drops no public CA: without an e-mail, Caddy 2.11.4's default is Let's Encrypt only (verified).

Caddy state (ACME account + certificates) lives in `./CaddyData` and must survive updates, or Let's Encrypt rate limits are hit on re-issue. The admin-facing guide is `wiki-git/Installation.md`. It embeds this compose file verbatim as the reference setup. `scripts/check-compose-wiki-sync.py` fails when the two differ, so change them together; it runs in `compose-wiki-sync.yml` on the pull request that touches either file (#1431). It replaced an xUnit test that ran only after merge, inside the server build that publishes the image. `scripts/local-docker-build.ps1` sets `HSM_DOMAIN=localhost` and publishes Caddy on `127.0.0.1` only.

What the HSM side relies on behind Caddy:

- **UI vs Sensor API split:** decided by listener port (`Connection.LocalPort`); Caddy keeps them apart by forwarding each site to its own upstream port.
- **Request scheme and host:** the upstream hop is HTTPS, so `Request.Scheme` is `https`: no HTTPS redirect, HSTS is still sent, and cookies stay `Secure`. Caddy passes the original `Host` through, so the agent-bundle fallback address (`AgentConnectionResolver`) is the public one with `SensorPort`.
- **Client IP (#1427):** HSM restores it from Caddy's `X-Forwarded-For` through `UseForwardedHeaders`, registered first in `ConfigureMiddleware`, only when `Kestrel.TrustedProxies` is non-empty. It uses `XForwardedFor` only (the upstream hop is HTTPS already, so the scheme is never rewritten), `ForwardLimit = 1`, and the framework's implicit loopback trust cleared.
  - The compose file sets `Kestrel__TrustedProxies__0: 'attached-networks'`. At startup HSM expands it to the networks of its own non-loopback interfaces (`TrustedProxyOptionsFactory`, masked to network addresses) and logs the result. In the container that is the compose network only. `app` publishes no ports, so from outside the host only Caddy reaches it. Caddy overwrites incoming `X-Forwarded-For` because no `trusted_proxies` is configured, so a remote client cannot inject an address.
  - A fixed compose subnet was tried and rejected: Docker hands out `/16`s from `172.17–172.31` to every compose project, so any pinned range collides on a busy host. It did on the development machine: "Pool overlaps with other one on this address space".
  - `TrustedProxies` is read **from the environment only** (`KestrelConfig.ReadTrustedProxies`: indexed `Kestrel__TrustedProxies__0=...` or a comma-separated `Kestrel__TrustedProxies=a,b`; blank entries dropped). The reason: `ServerConfig` rewrites `appsettings.json` on every start. `[JsonIgnore]` must keep that resave from persisting the environment's copy, because the settings file is registered after the environment provider and a persisted copy would override it later. So a hand-written file value could not survive the resave. It is ignored and reported once at `Error` level with its value (`FindTrustedProxiesInSettingsFile`); the same start's resave removes it from the file. IPv4 entries must be full dotted quads, because `IPAddress.TryParse` shorthands such as `192.168` are rejected. A non-Docker deployment behind its own proxy sets the environment variable for the service.
  - `TrustedProxyOptionsFactory.Build` returns **null** when no entry resolved (e.g. `attached-networks` finding no interface; `Up` and `Unknown` interfaces both count, since container veths often report `Unknown`). `ForwardedHeadersMiddleware` treats two empty known lists as "accept from anyone", so the middleware is then not registered and a warning is logged. The effective trust, or its absence, is logged at every start. `/0` and IPv4-mapped entries are rejected or normalised to IPv4, because the middleware maps only the peer.
  - Spoofing guards: Caddy (no `trusted_proxies`) replaces a client-sent `X-Forwarded-For`, verified on 2.11.4. Independently, `ForwardLimit = 1` takes only the rightmost entry, which is pinned by a test. Raising `ForwardLimit` for an extra hop needs that hop's own sanitising first.
  - **Residual risk:** `attached-networks` trusts the whole compose subnet, bridge gateway included. On Linux the bridge is routable from the host, so any process on the Docker host, and any container later joined to this network, can set the recorded client IP with one header. This does not grant access, which such callers had anyway; it lets them forge the IP data: token-audit `Source`, the key's last IP, and the invalid-attempt limiter buckets. Narrowing the trust to Caddy alone needs its address, which changes when its container is recreated, so it was not done. The recovery override publishes a port and therefore sets `Kestrel__TrustedProxies__0: ''`. On `network_mode: host` or macvlan the keyword would trust the whole LAN, so it is meant only for the bundled compose network.
  - The token-audit source omits the port behind the proxy (`X-Forwarded-For` has none), instead of recording `:0`. `TelemetryCollector` reads only `Connection.RemoteIpAddress`; its old raw `X-Forwarded-For` fallback, which would trust client-supplied entries, is removed.
  - Empty (`docker run`, direct access): no forwarded headers are honoured, same as before.
  - With it, the per-source invariant of `ApiTokenInvalidAttemptLimiter`, the token audit source and the key telemetry `RemoteIP` hold behind the proxy as they did without it.
- **TLS client compatibility:** Caddy's TLS 1.2 defaults are AEAD-only, while Kestrel used the platform (OpenSSL) list with CBC suites. Caddy issues ECDSA P-256 certificates by default (Let's Encrypt and its internal CA), and Windows 7 / Server 2008 R2 Schannel does offer `TLS_ECDHE_ECDSA_WITH_AES_*_GCM`, so `net472` collectors on those systems are expected to negotiate. This is not verified on a real Windows 7 client. If one fails, add an explicit `tls { ciphers ... }` to the sites.
- **When Caddy is down:** `app` publishes nothing, so Caddy is a single point of failure for the UI **and** for ingestion. The way around it is the "without Caddy" mode, `docker-compose.direct.yml`: `app` alone with its ports published and no `Kestrel__TrustedProxies`, so no forwarded-header trust.
- **Caddy → HSM hop:** `tls_insecure_skip_verify` means unauthenticated TLS carrying access keys. That is acceptable only because the compose network has two members and Docker's embedded DNS answers for `app`.
- **HSTS:** `AddHsts` (`Preload`, `IncludeSubDomains`) was always sent, but browsers ignore it over an untrusted certificate. With a Let's Encrypt certificate it now takes effect and pins `HSM_DOMAIN` and its subdomains to HTTPS for the max-age.
- **No access log in Caddy on purpose:** the default JSON log records request headers, and the collector's `Key` header is not among Caddy's redacted ones, so every access key would land on disk. HSM itself records client IPs since #1427.
- **HTTP/3 is off** (`servers { protocols h1 h2 }`): Caddy would advertise it via `Alt-Svc` on UDP ports the compose file does not publish, and every new browser connection would pay for a failed QUIC attempt.
- **Startup (#1431):** `app` has a healthcheck (`bash` `/dev/tcp` to 44330; Kestrel opens its ports only after the database load), and `caddy` depends on it with `condition: service_healthy`, so `docker compose up` starts Caddy only when HSM is ready. Verified live. **The budget is the upper bound on database-load time:** `start_period` 10 min plus `retries` 180 × `interval` 10 s = 40 min. Past it, `app` is `unhealthy`, `up` fails with "dependency failed to start", and Caddy is never created; `restart` cannot help a container that never started. Hence the large `retries`: the port never closes once open, so the budget costs nothing in steady state. The probe needs `bash` in the server image (Debian-based `aspnet:8.0`, see `.github/docker/dockerfile_deps`) and hardcodes 44330, like the Caddyfile. This gates only the initial `up`; for a later restart of `app` alone, `lb_try_duration 30s` makes Caddy wait for it instead of answering 502. Retries cover dial failures, so no request is sent twice.
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
