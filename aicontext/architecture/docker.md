# Docker Setup

> Owner: shared | Last reviewed: 2026-11-03 | Canonical: yes

## Production Deployment

docker-compose.yml runs HSM behind the published hsmonitoring/hsm-caddy:2.11.4-7 image:

- app serves HTTPS on one port inside the compose network (Kestrel.Port, default 443) and publishes no ports: the Web UI and the Sensor API share the single listener (#1590).
- caddy provides client-facing TLS, publishes ports 80, 443, 44330, and 44333, and proxies every site to the SAME single HSM listener. 443 is the primary address; 44330/44333 are deprecated aliases kept through the deprecation window for clients still pointed at the old two-port scheme — the app logs a rate-limited warning for requests arriving by them, and a follow-up release removes both the aliases and their port mappings. The image contains Caddy 2.11.4 with Cloudflare DNS module v0.2.4 and dynv6 module built from commit 71fad600afb29911aa04cbfd99f7d5d878c3bc48. Operators do not build it locally.
- Pull requests build and test without publishing. The trusted-master workflow publishes only after checks pass. Versioned tags are immutable: each Caddy source/config/module change increments the workflow version and matching compose tag; existing tags cannot be overwritten. The latest tag moves only when a new version publishes; it is pushed just before the version tag, so a publish that fails halfway leaves the version absent and the next run completes it. A run whose tag is already on Docker Hub skips publication (e.g. a compose-only edit) unless `caddy/` changed after the current `VERSION` value first appeared on the first-parent history, which fails and asks for a bump — on pull requests too, before merge; returning `VERSION` to an older published value is not a bump. Only the publish job declares the `DockerHub` environment, so PR runs never see the Docker Hub credentials.
- For repository development, scripts/local-docker-build.ps1 builds caddy/ as hsm-caddy:local and uses a generated Compose override. Operator installs use the published versioned image and need no local Caddy build.
- The Caddy configuration ships in the image as /etc/caddy/Caddyfile, with an entrypoint that validates settings and selects the TLS snippet. Compose configures image, environment, ports, and persistent data mounts.
- Catch-all sites and default_sni/fallback_sni retain clients that use an IP address or another hostname. They receive the certificate for HSM_DOMAIN, which may not match their chosen address.
- persist_config off prevents Caddy from saving expanded configuration. Caddy access logging is omitted because request headers can include HSM access keys.

The required .env settings are HSM_DOMAIN and HSM_CERTIFICATE. The entrypoint validates them before Caddy starts. DNS challenge additionally needs HSM_DNS_PROVIDER=cloudflare with CF_API_TOKEN, or HSM_DNS_PROVIDER=dynv6 with DYNV6_API_TOKEN. Cloudflare's token needs Zone DNS Edit and Zone Zone Read for the selected zone. Tokens are read from the container environment and are not expanded into the Caddyfile or persisted Caddy state. However, docker compose config renders interpolated values; treat its output as secret.

The optional HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN (#1585) covers installs whose provider token is scoped to a stub zone instead of the certificate name's zone (SoftFx admins issue Cloudflare tokens for `soft-fx.dev` and delegate each challenge name there with a CNAME). A non-blank value makes the entrypoint select the `-override` variant of the provider snippet, so Caddy writes the DNS-01 TXT record at the delegated alias (e.g. `_acme-challenge.hsm.dev.soft-fx.dev`) instead of `_acme-challenge.<HSM_DOMAIN>`; Let's Encrypt follows the CNAME and still issues the certificate for HSM_DOMAIN. The value is validated as a bare DNS name, is ignored outside letsencrypt-dns, and is per-install: each domain/machine has its own alias. It applies to the single HSM_DOMAIN name only — the Caddyfile has no per-site override mapping (a future public certificate for another site would need its own `dns_challenge_override_domain` per site block).

| Mode | Mechanism | Requirements and behavior |
|---|---|---|
| letsencrypt-http | ACME HTTP-01/TLS-ALPN | Public DNS for HSM_DOMAIN must point to this host; ports 80/443 must be reachable. |
| letsencrypt | Alias for letsencrypt-http | Preserves existing deployments. |
| letsencrypt-dns | ACME DNS-01 via Cloudflare or dynv6 | Provider token required; inbound port 80 is not needed. DNS validation can issue behind CGNAT but does not provide external network access. Optional HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN writes the TXT record at a stub-zone alias (#1585). |
| self-signed | Caddy internal CA | Clients must trust that CA or allow untrusted TLS. |
| custom | /certs/cert.pem and /certs/key.pem | Full chain and matching key mounted read-only from CaddyCertificates/; renewal is external and requires restarting Caddy. |
| Without Caddy | docker-compose.direct.yml | HSM's existing PFX certificate is served directly; this workflow is unchanged. |

There is no automatic fallback between certificate modes. A failed issuance does not silently select another certificate. Caddy state (ACME account and managed certificates) lives in ./CaddyData; its internal CA root is ./CaddyData/caddy/pki/authorities/local/root.crt. Custom certificate files are mounted from ./CaddyCertificates read-only.

Advanced operators may mount a customized Caddyfile at /etc/caddy/Caddyfile through docker-compose.override.yml. Preserve the entrypoint-selected HSM_TLS_SNIPPET import, and when a new image changes the bundled Caddyfile (as 2.11.4-3 did for the Host pass-through, 2.11.4-5 for the single 443 upstream of #1590, 2.11.4-6 for the removal of the log-store routes, #1594/ADR-0015, and 2.11.4-7 for the stub-zone DNS-01 override snippets, #1585 — a stale pre-2.11.4-5 copy still dials `app:44330`/`app:44333`, which the app no longer listens on, so sensor ingestion through such a copy breaks until re-merged), re-merge the customized copy with the new bundled one: a stale copy silently keeps the old shape — a copy still carrying the removed log-store routes keeps loading, and `/logs/*` stays refused on it until re-merged: the copy's `forward_auth` gate calls `/logs-auth`, which the app no longer serves, so the app's 404/redirect is passed back to the client (502 only while the app itself is down). With a pre-2.11.4-3 copy, login redirects stay broken. A copy merged before 2.11.4-7 keeps loading, but the entrypoint's `-override` snippet selection fails on it when HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN is set (undefined snippet), so the stub-zone scheme needs the re-merged file. Restarting the service reruns the entrypoint. For an in-place reload, invoke the entrypoint wrapper: docker exec hsm-caddy hsm-caddy-entrypoint caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile. Calling caddy reload directly does not initialize the selected TLS snippet. The admin guide is wiki-git/Installation.md; its embedded compose reference must remain byte-for-byte synchronized with docker-compose.yml, enforced by scripts/check-compose-wiki-sync.py.

What the HSM side relies on behind Caddy:

- **UI vs Sensor API split:** there is none — since #1590 there is one listener, and both surfaces answer on it (measured before the collapse on a live 3.41.x server: both ports served both surfaces). The former SitePort-only guards (management-area listener gate, MCP gate, Swagger gate) are retired with the second listener; their auth-level rules (fail-closed `/api/v1` allow-list, bearer-only management policy, MCP endpoint policy) are unchanged and carry the isolation alone (ADR-0014). The 44330/44333 public ports survive only as deprecated aliases of the same upstream through the deprecation window; a request by one carries its port in the Host header, which `LegacyPortDeprecationMiddleware` logs as a rate-limited warning — the operator's straggler signal.
- **Request scheme and host:** the upstream hop is HTTPS, so `Request.Scheme` is `https`: no HTTPS redirect, HSTS is still sent, and cookies stay `Secure`. The bundled Caddyfile passes the original client `Host` through explicitly (`header_up Host {hostport}`, pinned image 2.11.4-3): Caddy ≥ 2.11 otherwise replaces it with the upstream address (`app:443`), which only Docker's DNS resolves — HSM builds absolute redirects (login) from the request Host, so browsers would be sent to an unreachable name (measured: `Location: https://app:44333/Account/Index?ReturnUrl=%2F` on 2.11.4-2; fixed by the mounted-Caddyfile workaround locally until 2.11.4-3 bundled it). With it, the agent-bundle fallback address (`AgentConnectionResolver`) is the request host's port — or 443 when the host carries none or a deprecated alias.
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
- **Startup (#1431, #1465):** the check now lives in the **image** (see "Image healthcheck" below) and is repeated verbatim in the compose file; `caddy` depends on it with `condition: service_healthy`, so `docker compose up` starts Caddy only when HSM serves. Verified live. **The budget is the upper bound on database-load time:** ≈ 20 min on Docker Engine 25+ (`start_period` + (`retries` - 1) × `interval` = 10 + 2×5, because the daemon probes every `start-interval` while the container is `starting`), up to 25 min on a pre-25 daemon (`start_period` + `retries` × `interval` = 10 + 3×5; the same arithmetic gave 40 min with the 10 s / 180-retry TCP probe and 11.5 min with #1465's original 30 s `interval`). Past it, `app` is `unhealthy`, `up` fails with "dependency failed to start", and Caddy is never created; `restart` cannot help a container that never started. A deployment whose database needs longer changes `start_period`. Which way depends on the image: with one that carries the healthcheck (the first release after 3.41.5), overriding that single field in its own compose file — or `--health-start-period` on `docker run` — is enough, because the daemon keeps the image's other fields (verified in #1465); with `:latest` = 3.41.5 or any older image there is no image copy to fall back on, so a trimmed override leaves the container with NO healthcheck and `depends_on` fails at once — keep the full `healthcheck:` block and change `start_period` inside it. Moving the repo defaults is the maintainer case — then both copies move together, because `scripts/check-healthcheck-sync.py` compares `test`, `interval`, `timeout`, `start_period` and `retries`. This gates only the initial `up`; for a later restart of `app` alone, `lb_try_duration 30s` makes Caddy wait for it instead of answering 502. Retries cover dial failures, so no request is sent twice.
- **HTTP on port 80:** Caddy redirects `http://<HSM_DOMAIN>/` to `https://<HSM_DOMAIN>/` (the UI on 443), verified live.

## Image healthcheck (#1465)

The published server image declares its own `HEALTHCHECK`, so `docker ps`, `depends_on: condition: service_healthy` and any orchestrator see HSM's health in **every** deployment — the bundled compose file, `docker-compose.direct.yml`, a hand-written compose, a plain `docker run`. Before this the only check was in this repo's compose file, so a host running an older compose (the real garage-server deployment) had no health at all.

**Mechanism.** The .NET SDK's container publishing cannot emit a `HEALTHCHECK`: there is no such property in `Microsoft.NET.Build.Containers` (checked in SDK 8.0.420 and 9.0.315), and the feature request was closed as won't-do — "Docker-only and has no support in OCI images" (`dotnet/sdk-container-builds#316`). The check is therefore one final layer, `docker_scripts/HSMserver/Dockerfile.healthcheck`, built with `--build-arg BASE_IMAGE=<the image just built>` and re-tagged onto the same tags. **Both** publishing paths apply that one file: `server-build.yml`'s `publish-docker-image` job (after the SDK publish, before the guards, so what is verified is what is pushed) and `scripts/local-docker-build.ps1` (after `Dockerfile.local`). A CI step fails the lane if a pushed tag carries no HSM healthcheck or the base image lost `wget`. The alternative — putting `HEALTHCHECK` in the deps base image — was rejected: local builds pull the *published* deps image, so the local image would differ from the CI one until the deps image is republished.

**The check** (`interval` 5 min, `timeout` 5 s, `retries` 3, `start_period` 10 min; #1465 shipped a 30 s `interval`, raised to 5 min in 2026-10):

```
wget --quiet --tries=1 --timeout=4 --no-check-certificate --output-document=/dev/null \
    https://127.0.0.1:443/api/sensors/testConnection
```

**What healthy means.** Kestrel starts listening only after startup completed: `Program.cs` awaits `InitStorages()`, which resolves `IUserManager` → `TreeValuesCache`, whose constructor loads the tree from LevelDB, before `app.Run()`. So a 200 means the database was loaded, the server certificate was usable for the handshake, and the request pipeline is serving.

| Observed | `docker ps` | Meaning |
|---|---|---|
| connection refused | `starting` (then `unhealthy` past the budget) | process alive, still loading the database (or Kestrel failed to bind) |
| 200 | `healthy` | listening **and** serving |
| probe times out | `unhealthy` | listening but wedged (stopped or deadlocked process): the socket stays open, which the previous TCP-connect probe reported as healthy |
| non-2xx (`wget` exit 8) | `unhealthy` | serving, but the endpoint no longer answers |

Verified live on the locally built image: `starting` → `healthy` at the first probe; with the server process frozen (`pkill -STOP`, socket still listening) three probes failed ~4 s each and `docker ps` showed `unhealthy` 99 s later, while a plain TCP connect to 44330 still succeeded in 0.03 s — exactly the case the old probe called healthy; after `pkill -CONT` the next probe returned it to `healthy` (24 s). Killing the process exits the container (`Exited (137)`), so it cannot stay green either. The timings above are from #1465's 30 s `interval`; with the current 5 min `interval` the same wedge is reported after up to `retries` × `interval` ≈ 15 min. A container watched by the Linux probe's `Health` sensor ('unhealthy for 5 min' alert) now surfaces a wedge to that alert in roughly 15–20 min (10–15 min to turn unhealthy plus the 5-min hold) instead of about 6.5 min.

`/api/sensors/testConnection` is the endpoint collectors already use for their connection test (`SensorsController`, `[AllowAnonymous]`, a constant `Ok()`): anonymous, no database read, no new public surface, no new key material inside the image. It reports no per-subsystem readiness on purpose — HSM has no state in which it listens without the database loaded, so there is nothing finer to report.

**TLS.** Kestrel serves HTTPS on the single port with the server's own certificate, self-signed by default (`KestrelListenOptions` always calls `UseHttps`), and has no plaintext mode. The probe skips verification (`--no-check-certificate`) for its own loopback connection, so it never depends on the certificate being trusted inside the container and behaves the same in the Caddy-fronted and the direct deployments — it never leaves the container. `--timeout=4` keeps a stalled connection inside docker's 5 s budget, so a wedged server fails cleanly instead of being killed mid-probe.

**Port.** 443 is hardcoded, like the Caddyfile's upstream (`KestrelConfig.DefaultPort`, #1590). A deployment that moves the port in `Config/appsettings.json` (`Kestrel.Port`; the pre-#1590 `SensorPort`/`SitePort` keys are ignored with a startup warning) overrides the check (`healthcheck:` in compose, `--health-cmd` in `docker run`). Overriding a single field is enough: the daemon keeps the image's values for the fields the container does not set (verified with `docker run --health-start-period=10s`, which left the image's `Test`, `Interval`, `Timeout` and `Retries` in place).

**Why the compose file repeats it.** `depends_on: condition: service_healthy` fails immediately when *neither* the image nor the compose file defines a check — "dependency failed to start: container hsm-server has no healthcheck configured" (verified). Every image published before #1465, including the current `:latest` (3.41.5), has none, so dropping the compose block would break the bundled stack — new installs included, since they pull `:latest` — until the next image release. The block is therefore kept **byte-identical** to the image's, and `scripts/check-healthcheck-sync.py` (run in `compose-wiki-sync.yml`, on every PR) fails when the two differ. Verified live: the new compose file + the published 3.41.5 image gate Caddy correctly (`app` Waiting → Healthy → `caddy` Starting, 7 s). The one image generation it cannot cover is a much older one: `:latest` as built in June 2026 has no `wget` either ("wget: not found"), so there the probe fails until the budget runs out; such a deployment keeps the compose file that matches its image.

**Measured cold starts** (local image, Docker Desktop on Windows, SSD, #1465):

| Data | container start → "Now listening" | `docker ps` healthy |
|---|---|---|
| empty `Databases` | ~1.5 s | +5.2 s (first probe) |
| 212 MB real database, 34 products | 1.4 s (`TreeValuesCache initialized` 0.4 s before it) | +5.1 s (first probe) |

For a current-format database the startup cost is the tree — products, sensors, policies — not the history: the `SensorValuesV2_*` interval databases open lazily in the background. `start_period` 10 min is therefore a very large margin, kept for a slow disk and a much larger tree. On Docker Engine 25 or newer the daemon probes every 5 s inside the start period (its default `--start-interval`), so a healthy container is reported within seconds — as long as HSM is listening before `start_period` ends; past that the next probe can be up to one `interval` (5 min) away, so `app` can stay `starting` and hold Caddy back that much longer. An older daemon waits for the first 5 min `interval` instead, which delays Caddy by that much.

Two limits of a start period sized this way:

- **Legacy-format history migrates before HSM listens.** `TreeValuesCache` calls `MigrateDatabseV2()` synchronously before `app.Run()`, and it reads and rewrites every value of every old `SensorValues_*` database, so that one start grows with history, not with the tree. On such an install the ~20 min budget (up to 25 min on a pre-25 daemon) can run out: `app` goes `unhealthy` and `docker compose up` reports "dependency failed to start". The migration keeps running inside the container, but a second `up` **while it is still running fails the same way** — `up` does not recreate an unchanged container, and an `unhealthy` dependency is refused at once. The recovery is to wait for the migration to finish (`Now listening` in the `app` log, `docker ps` healthy at the next probe, up to 5 min later) and then run `docker compose up -d` again, or to raise `start_period` in the compose file's full `healthcheck:` block before the upgrade. The old 180-retry budget absorbed this without intervention; that is the price of #1465's timings.
- **A probe failure inside the start period never marks the container unhealthy**, so a wedge in the first 10 minutes after a restart stays invisible.

## Log files

The app logs to text files under `Logs/` — `errorfile`, `allfile`, and `aspfile` in `src/server/HSMServer/nlog.config` (`Logs/Archives/` holds their rotated history; HSMPingModule adds two more targets where co-located). Every target writes a **static** file name and rotates it daily and at 100 MB into `Logs/Archives/` with `archiveNumbering="DateAndSequence"`, keeping at most 60 archive files per target — ~6 GiB worst case each, so the server's `Logs/` bounds at ~18 GiB worst case; size the disk for the total, not for one target. This retention contract is a hard requirement, not style: NLog archives a file — and applies `maxArchiveFiles` cleanup — only when rotating a static `fileName`; with a dated name every day opens a new file and yesterday's file is abandoned in `Logs/` forever (the 2026-10 dev incident accumulated 49 GiB that way, #1583). ADR-0011 is the decision record; `NlogConfigTests` pins the static names and the rotation limits of both configs. One retention trade-off to know: for `errorfile` the 60-archive cap means 60 files, not ~60 days — a mid-day 100 MB size rotation during an error flood consumes archive slots, so ~6 GiB of flood errors evicts the pre-flood error history (ADR-0011, Consequences).

The searchable log-store pipeline that used to sit next to these files (store + shipper containers, an app-side JSON spool, and admin-gated read routes — #1470/#1580) was removed in #1594 (ADR-0015). Nothing replaces it: the text files and their archives are the only log source. The JSON spool files the removed pipeline wrote (`Logs/HSM-structured-log-current.json` and up to 60 × 100 MB of `Logs/Archives/HSM-structured-log-*.json`) are orphaned: NLog cleans archives per target on rotation and their `jsonfile` target is gone, so nothing will ever remove them (the abandoned-files failure mode of ADR-0011/#1583, by removal instead of misconfiguration). They sit outside the ~18 GiB text bound until deleted once by hand after upgrading — `rm -f Logs/HSM-structured-log-current.json Logs/Archives/HSM-structured-log-*.json`.

## Ports

| Port | Purpose |
|---|---|
| 443 | Everything — Web UI, Sensor API (`/api/sensors/*`), management API, MCP, downloads (#1590) |
| 44330 | Deprecated alias of 443 (Sensor API legacy address), kept through the deprecation window |
| 44333 | Deprecated alias of 443 (Web UI legacy address), kept through the deprecation window |
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

Kestrel continues to serve HTTPS on the single port with `Config/<ServerCertificate.Name>` (PFX, optional `Key` password) or, when absent, the bundled self-signed `default.server.pfx`. The direct compose file and standalone `docker run` continue to present that PFX certificate to clients. In the bundled compose setup, only Caddy sees the HSM certificate and presents the selected HTTP-ACME, DNS-ACME, internal, or custom PEM certificate to clients. The existing PFX workflow is unchanged.

## Notes

- No external database service needed — LevelDB is embedded
- Server listens on the single port via Kestrel (`Kestrel.Port`, default 443; #1590)
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
