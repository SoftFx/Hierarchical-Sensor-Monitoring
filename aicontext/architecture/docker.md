# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

docker-compose.yml runs HSM behind the published hsmonitoring/hsm-caddy:2.11.4-1 image:

- app serves HTTPS on ports 44330 and 44333 inside the compose network and publishes no ports.
- caddy provides client-facing TLS, publishes ports 80, 443, 44330, and 44333, and proxies to the matching HSM listener. The image contains Caddy 2.11.4 with Cloudflare DNS module v0.2.4 and dynv6 module built from commit 71fad600afb29911aa04cbfd99f7d5d878c3bc48. Operators do not build it locally.
- Pull requests build and test without publishing. The trusted-master workflow publishes only after checks pass. Versioned tags are immutable: each Caddy source/config/module change increments the workflow version and matching compose tag; existing tags cannot be overwritten. The latest tag advances only after a new version publishes.
- For repository development, scripts/local-docker-build.ps1 builds caddy/ as hsm-caddy:local and uses a generated Compose override. Operator installs use the published versioned image and need no local Caddy build.
- The Caddy configuration ships in the image as /etc/caddy/Caddyfile, with an entrypoint that validates settings and selects the TLS snippet. Compose configures image, environment, ports, and persistent data mounts.
- Catch-all sites and default_sni/fallback_sni retain clients that use an IP address or another hostname. They receive the certificate for HSM_DOMAIN, which may not match their chosen address.
- persist_config off prevents Caddy from saving expanded configuration. Caddy access logging is omitted because request headers can include HSM access keys.

The required .env settings are HSM_DOMAIN and HSM_CERTIFICATE. The entrypoint validates them before Caddy starts. DNS challenge additionally needs HSM_DNS_PROVIDER=cloudflare with CF_API_TOKEN, or HSM_DNS_PROVIDER=dynv6 with DYNV6_API_TOKEN. Cloudflare's token needs Zone DNS Edit and Zone Zone Read for the selected zone. Tokens are read from the container environment and are not expanded into the Caddyfile or persisted Caddy state. However, docker compose config renders interpolated values; treat its output as secret.

| Mode | Mechanism | Requirements and behavior |
|---|---|---|
| letsencrypt-http | ACME HTTP-01/TLS-ALPN | Public DNS for HSM_DOMAIN must point to this host; ports 80/443 must be reachable. |
| letsencrypt | Alias for letsencrypt-http | Preserves existing deployments. |
| letsencrypt-dns | ACME DNS-01 via Cloudflare or dynv6 | Provider token required; inbound port 80 is not needed. DNS validation can issue behind CGNAT but does not provide external network access. |
| self-signed | Caddy internal CA | Clients must trust that CA or allow untrusted TLS. |
| custom | /certs/cert.pem and /certs/key.pem | Full chain and matching key mounted read-only from CaddyCertificates/; renewal is external and requires restarting Caddy. |
| Without Caddy | docker-compose.direct.yml | HSM's existing PFX certificate is served directly; this workflow is unchanged. |

There is no automatic fallback between certificate modes. A failed issuance does not silently select another certificate. Caddy state (ACME account and managed certificates) lives in ./CaddyData; its internal CA root is ./CaddyData/caddy/pki/authorities/local/root.crt. Custom certificate files are mounted from ./CaddyCertificates read-only.

Advanced operators may mount a customized Caddyfile at /etc/caddy/Caddyfile through docker-compose.override.yml. Preserve the entrypoint-selected HSM_TLS_SNIPPET import. Restarting the service reruns the entrypoint. For an in-place reload, invoke the entrypoint wrapper: docker exec hsm-caddy hsm-caddy-entrypoint caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile. Calling caddy reload directly does not initialize the selected TLS snippet. The admin guide is wiki-git/Installation.md; its embedded compose reference must remain byte-for-byte synchronized with docker-compose.yml, enforced by scripts/check-compose-wiki-sync.py.

What the HSM side relies on behind Caddy:

- **UI vs Sensor API split:** there is none. Both listeners share one routing table, so the Sensor API answers on the SitePort and the UI's pages are served on the SensorPort too (measured on a live 3.41.x server: `/api/sensors/testConnection`, `/api/sensors/commands` and `/Account/Index` all return 200 on both). What `Connection.LocalPort` decides is narrower and one-way: the `/api/v1` management area (`ManagementApiGuardMiddleware`), MCP (`McpSitePortOnlyMiddleware`) and Swagger (`SwaggerSitePortOnlyMiddleware`) are **SitePort-only** and 404 on the SensorPort, and `UserProcessorMiddleware` resolves the cookie user only on the SitePort, so a browser cannot sign in on the SensorPort. Nothing is SensorPort-only. For a reverse proxy this means forwarding everything to the **SitePort** serves both surfaces over one public port; forwarding the UI to the SensorPort would break sign-in. The compose file still maps each public port to the matching upstream so that clients already pointed at 44330 keep working.
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
| 80 | HTTP-01 challenge and HTTP-to-HTTPS redirect; not needed for DNS-01 issuance (compose/Caddy only) |

## Volumes

| Volume | Purpose |
|---|---|
| `Logs` | NLog output files |
| `Config` | Server configuration (TLS, Telegram, backup settings) |
| `Databases` | LevelDB data files (sensor history, metadata) |
| `DatabasesBackups` | Automated SFTP backup snapshots |
| `CaddyData` | Caddy certificates and ACME account (compose) |
| `CaddyCertificates` | Optional custom certificate chain and key, mounted read-only at `/certs` |

## TLS

Kestrel continues to serve HTTPS on both ports with `Config/<ServerCertificate.Name>` (PFX, optional `Key` password) or, when absent, the bundled self-signed `default.server.pfx`. The direct compose file and standalone `docker run` continue to present that PFX certificate to clients. In the bundled compose setup, only Caddy sees the HSM certificate and presents the selected HTTP-ACME, DNS-ACME, internal, or custom PEM certificate to clients. The existing PFX workflow is unchanged.

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
