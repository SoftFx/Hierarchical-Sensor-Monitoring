# Installation

HSM Server is distributed as a Docker image. This page covers all deployment methods.

---

## Prerequisites

- [Docker](https://www.docker.com/) with Docker Compose v2.23.1 or newer
- Ports `44330` and `44333` available on the host, plus `80` and `443` for the automatic certificate. Check that nothing else on the host (another web server or proxy) already uses `80`/`443`, otherwise Caddy cannot start. With `HSM_DOMAIN` set to an IP address, `80`/`443` are not needed and you can remove those two lines from the `caddy` service.

---

## Method 1 — Docker Compose (recommended)

The compose file runs two containers: the HSM server and [Caddy](https://caddyserver.com/), a web server in front of it. Caddy provides the certificate clients see and passes requests to HSM. Which certificate it uses is one explicit setting. Nothing switches automatically, so the server always does exactly what the settings say.

There are three ways to run HSM. You choose one, and switch between them by changing a setting:

| Mode | How | Certificate clients see |
|---|---|---|
| **Caddy + Let's Encrypt** | `HSM_CERTIFICATE=letsencrypt` | Free trusted certificate from Let's Encrypt, obtained and renewed automatically |
| **Caddy + self-signed** | `HSM_CERTIFICATE=self-signed` | Caddy's own certificate: for an IP address, an internal network, or while Let's Encrypt is unavailable |
| **Without Caddy** | `docker-compose.direct.yml` | HSM's own certificate, as before Caddy was added (see [Without Caddy](#without-caddy)) |

**1. Download the reference compose file** (full text below, in [Reference docker-compose.yml](#reference-docker-composeyml)):

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
```

Do not write your own compose file or put a different proxy in front: the HSM side of the setup depends on exactly this Caddy configuration. The edits described on this page (removing ports `80`/`443`, an e-mail for Let's Encrypt) are fine.

**2. Create the settings file.** Create a file named `.env` next to `docker-compose.yml` with two lines:

```dotenv
HSM_DOMAIN=hsm.example.com
HSM_CERTIFICATE=letsencrypt
```

| Setting | Value |
|---|---|
| `HSM_DOMAIN` | The address clients use: a bare host name or IP address. No `https://`, no port, no trailing slash. |
| `HSM_CERTIFICATE` | `letsencrypt`: needs a public DNS name in `HSM_DOMAIN` that points to this machine, and ports `80`/`443` reachable from the internet.<br>`self-signed`: always works; browsers show a warning, and collectors/agents need "allow untrusted certificate". Use it for an IP address or a name that is not reachable from the internet. |

Both are required: without them every `docker compose` command (`up`, `down`, `logs`, `pull`) stops with an error, so keep the file next to the compose file. If it is lost, you can still stop the stack with `HSM_DOMAIN=x HSM_CERTIFICATE=x docker compose down`.

For `letsencrypt`, use a dedicated name such as `hsm.example.com`, not your organization's main domain. Once the server has a trusted certificate, browsers are told to use HTTPS for that name **and all its subdomains** for a month, and this cannot be undone remotely.

**3. Start the server:**

```bash
docker compose up -d
```

**4. Open the web UI:** `https://<HSM_DOMAIN>` or `https://<HSM_DOMAIN>:44333`.

Default credentials: login `default`, password `default`. **Change the password immediately.**

Collectors and agents connect to `https://<HSM_DOMAIN>:44330`, as before.

**5. Check the certificate.** Run on any machine:

```bash
openssl s_client -connect hsm.example.com:443 -servername hsm.example.com </dev/null 2>/dev/null | openssl x509 -noout -issuer -enddate
```

The issuer shows the mode: `Let's Encrypt` for `letsencrypt`, `Caddy Local Authority` for `self-signed`. `notAfter` shows when the certificate expires. If there is no output, Caddy has no certificate yet; see `docker logs hsm-caddy`.

Optionally, give Let's Encrypt an e-mail: it then warns you before a certificate expires. Add `email admin@example.com` as the first line inside the leading `{ ... }` block of the `caddyfile` section.

### How the certificate is obtained and renewed

**`letsencrypt`:**

1. **At start**, Caddy looks for a valid certificate in `CaddyData`. If there is one, it is used immediately.
2. Otherwise Caddy requests one from Let's Encrypt, which checks the domain through port `80`/`443`. This takes seconds; a failed check can take a few minutes.
   - **Success:** the certificate is stored in `CaddyData` and served.
   - **Failure:** there is **no certificate**, and HTTPS connections to the server fail: browsers, collectors and agents alike. `docker logs hsm-caddy` shows the reason. Caddy keeps retrying with growing pauses, and nothing else is tried: see [Switching the certificate mode](#switching-the-certificate-mode).
3. **Renewal:** Caddy checks the certificate every few minutes. It renews it about **30 days before** it expires (Let's Encrypt certificates are valid 90 days).
   - If a renewal fails, the current, still valid certificate keeps being served, and Caddy keeps retrying. You have until `notAfter` to react; the optional e-mail above warns you.

**`self-signed`:** Caddy issues its own certificate immediately at start and renews it automatically. It cannot fail.

### Switching the certificate mode

| Situation | What to do |
|---|---|
| New installation, Let's Encrypt cannot issue (no certificate, see `docker logs hsm-caddy`) | Fix the cause (DNS record, port `80` in the firewall), or switch to `self-signed` for now |
| Renewal keeps failing and `notAfter` is close | Fix the cause, or switch to `self-signed` before the certificate expires |
| The cause is fixed | Switch back to `letsencrypt` |
| Caddy does not start at all (port conflict, broken edit) | Run [without Caddy](#without-caddy) until it is fixed |

To switch, edit `HSM_CERTIFICATE` in `.env` and run:

```bash
docker compose up -d
```

Caddy restarts with the new mode. Collectors and agents with "allow untrusted certificate" keep working in both modes. **Browsers:** after the server has served a Let's Encrypt certificate, browsers that opened it refuse a self-signed one with **no way to continue**, for up to 30 days (HSTS). While in `self-signed` mode, the web UI is then reachable only from other browsers.

### Without Caddy

HSM serves HTTPS itself with its own certificate, exactly as before Caddy was added. Use this if you do not want Caddy, or while Caddy cannot start. Download the second compose file next to the first one. Both use the same data folders, so you can switch in both directions without losing anything:

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.direct.yml

docker compose down                                 # stop the Caddy stack
docker compose -f docker-compose.direct.yml up -d   # start HSM alone
```

and back:

```bash
docker compose -f docker-compose.direct.yml down
docker compose up -d
```

Collectors and agents keep using `https://<host>:44330`, with "allow untrusted certificate" unless you installed your own `.pfx` (see [Server Configuration](Server-Configuration)). Ports `80` and `443` are not used.

### Reference docker-compose.yml

This is the supported setup, the same file as [`docker-compose.yml`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker-compose.yml) in the repository:

```yaml
# HSM Server behind Caddy. Everyone runs the same stack with `docker compose up -d`.
#
# Caddy terminates TLS for clients and obtains/renews the certificate by itself. HSM keeps
# serving HTTPS with its own (self-signed by default) certificate, reachable only inside the
# compose network; Caddy connects to it without verifying that certificate.
# Two settings in a `.env` file next to this one (or in the shell); compose refuses to start
# without them:
#   HSM_DOMAIN=hsm.example.com       the address clients use (DNS name or IP address)
#   HSM_CERTIFICATE=letsencrypt      certificate from Let's Encrypt (public DNS name pointing here,
#                                    port 80 reachable from the internet); nothing else is tried
#   HSM_CERTIFICATE=self-signed      Caddy's own certificate (IP address, internal network, or
#                                    while Let's Encrypt is unavailable)
# Switching = edit .env and run `docker compose up -d`. Collectors and agents keep using
# https://<HSM_DOMAIN>:44330. To run without Caddy, use docker-compose.direct.yml instead.
#
# The image is published by CI (server-build.yml). To run a build from local sources instead,
# publish it to this exact tag first:
#   dotnet publish src/server/HSMServer/HSMServer.csproj -c Release --os linux --arch x64 \
#     -p:PublishProfile=DefaultContainer -p:ContainerImageName=hsmonitoring/hierarchical_sensor_monitoring
services:
  app:
    image: 'hsmonitoring/hierarchical_sensor_monitoring:latest'
    container_name: hsm-server
    restart: unless-stopped
    user: '0'
    # No ports: HSM is reachable only through caddy.
    healthcheck:
      # Kestrel opens its ports only after the database has loaded, so an open port means ready.
      test: ['CMD', 'bash', '-c', 'exec 3<>/dev/tcp/127.0.0.1/44330']
      interval: 10s
      timeout: 5s
      # "unhealthy" makes `up` skip caddy for good, so leave a generous budget: 10 min start
      # period + 180 failed probes (30 min). Once open, the port stays open.
      retries: 180
      start_period: 10m
    environment:
      # Trust X-Forwarded-For only from the compose network, whose only other member is caddy,
      # so HSM records the real client IP (token audit, invalid-attempt limiter, key telemetry).
      Kestrel__TrustedProxies__0: 'attached-networks'
    volumes:
      - ./Logs:/app/Logs                       # NLog output
      - ./Config:/app/Config                   # server config (TLS cert, Telegram, Agent settings)
      - ./Databases:/app/Databases             # embedded LevelDB (sensor history + metadata)
      - ./DatabasesBackups:/app/DatabasesBackups

  caddy:
    image: 'caddy:2.11.4'   # pinned: other-address clients rely on default_sni/fallback_sni; re-verify on bump
    container_name: hsm-caddy
    restart: unless-stopped
    depends_on:
      app:
        condition: service_healthy             # start serving only once HSM is ready
    environment:
      # Passed to Caddy (read as {$VAR} in the Caddyfile) rather than written into it: a changed
      # environment makes `docker compose up -d` recreate caddy, a changed inline config does not.
      HSM_DOMAIN: '${HSM_DOMAIN:?Set HSM_DOMAIN in .env next to docker-compose.yml - see the comment at the top}'
      HSM_CERTIFICATE: '${HSM_CERTIFICATE:?Set HSM_CERTIFICATE in .env to letsencrypt or self-signed - see the comment at the top}'
    ports:
      - '80:80'         # ACME HTTP challenge + redirect to https
      - '443:443'       # Web UI on the standard port
      - '44330:44330'   # Sensor API — collectors/agents send values here (https, /api/sensors/*)
      - '44333:44333'   # Web UI — browser dashboard, admin, agent download (https)
    configs:
      - source: caddyfile
        target: /etc/caddy/Caddyfile
    volumes:
      - ./CaddyData:/data                      # ACME account + certificates; keep it across updates

configs:
  caddyfile:
    content: |
      {
          # HTTP/3 would be advertised (Alt-Svc) on UDP ports that are not published.
          servers {
              protocols h1 h2
          }
          # Clients that reach this host by IP (no SNI) or by another name get the
          # HSM_DOMAIN certificate instead of a refused handshake.
          default_sni {$$HSM_DOMAIN}
          fallback_sni {$$HSM_DOMAIN}
      }
      # One certificate source, chosen by the admin (HSM_CERTIFICATE), never an automatic
      # fallback: a failed Let's Encrypt renewal must not replace a still-valid certificate.
      (tls-letsencrypt) {
          tls {
              issuer acme
          }
      }
      (tls-self-signed) {
          tls internal
      }
      (hsm_upstream) {
          transport http {
              tls_insecure_skip_verify
          }
          # Wait for HSM while it starts (database load) instead of answering 502.
          lb_try_duration 30s
      }
      {$$HSM_DOMAIN}, {$$HSM_DOMAIN}:44333 {
          import tls-{$$HSM_CERTIFICATE}
          reverse_proxy https://app:44333 {
              import hsm_upstream
          }
      }
      {$$HSM_DOMAIN}:44330 {
          import tls-{$$HSM_CERTIFICATE}
          reverse_proxy https://app:44330 {
              import hsm_upstream
          }
      }
      # Any other address of this host (IP, short name, old CNAME): existing collectors and
      # agent bundles keep working. The certificate does not match such a name, so those
      # clients need "allow untrusted certificate", as with the old self-signed setup.
      https://:443, https://:44333 {
          reverse_proxy https://app:44333 {
              import hsm_upstream
          }
      }
      https://:44330 {
          reverse_proxy https://app:44330 {
              import hsm_upstream
          }
      }
```

What must stay as it is, if you ever adapt it:

| Part | Why |
|---|---|
| No `ports:` on `app` | HSM must be reachable only through Caddy. |
| `reverse_proxy https://app:44333` and `https://app:44330` in separate sites | HSM tells the web UI and the Sensor API apart by the port a request arrives on. |
| `tls_insecure_skip_verify` | HSM still serves HTTPS with its own self-signed certificate; Caddy connects to it inside the compose network without checking it. |
| `import tls-{$HSM_CERTIFICATE}` with the `tls-letsencrypt` / `tls-self-signed` blocks | Exactly one certificate source, chosen in `.env`. There is no automatic fallback, so a failed Let's Encrypt renewal never replaces a still-valid certificate. |
| `HSM_DOMAIN` / `HSM_CERTIFICATE` in the `environment` of `caddy` | Caddy reads them at start. Because they are environment variables, changing `.env` and running `docker compose up -d` recreates Caddy with the new values. |
| `healthcheck` on `app` + `condition: service_healthy` | Caddy starts only when HSM has loaded its database. HSM gets up to 40 minutes for that. |
| `default_sni` / `fallback_sni` and the `https://:443`, `https://:44333`, `https://:44330` sites | Clients that reach the server by IP or by another name keep working. They receive the `HSM_DOMAIN` certificate, which does not match the name they use, so they need "allow untrusted certificate". |
| Public ports `44330` and `44333` | Collectors and agents connect to `https://<host>:44330`; downloaded agent bundles use this port. |
| Ports `80` and `443` | Required for `letsencrypt`: Let's Encrypt checks the domain through them. With `self-signed` you can remove both lines; the web UI is then available on `44333` only. |
| `./CaddyData:/data` | Keeps certificates across updates; without it Caddy requests new ones on every restart and hits Let's Encrypt rate limits. |
| `Kestrel__TrustedProxies__0: 'attached-networks'` | HSM trusts the client address that Caddy forwards only from the network of its own container, the compose network, whose only other member is Caddy. The web UI and the API-token audit then show the real client IP, and remote clients cannot choose that address. Processes on the Docker host itself, and containers you attach to this network, can. No subnet has to be picked, so it cannot collide with other Docker networks on the host. |
| `caddy:2.11.4` (pinned version) | Clients on other addresses depend on how Caddy picks a certificate; a new Caddy version is taken deliberately, after checking that behavior again. |

### Internal DNS name (not reachable from the internet)

Let's Encrypt cannot issue a certificate for a name it cannot reach: use `HSM_CERTIFICATE=self-signed`. The certificate then comes from Caddy's own certificate authority, which clients do not trust: browsers show a warning, and collectors/agents need "allow untrusted certificate". To avoid that, install Caddy's root certificate on the client machines. It is `CaddyData/caddy/pki/authorities/local/root.crt` next to the compose file.

### Moving an existing installation to Caddy

Nothing changes on the HSM side: it keeps its data, settings and own certificate.

**Before you stop the old stack**, check ports `80` and `443`:

- **They must be free on the host.** If they are taken, Caddy cannot start after the old stack is already down. In that case, run [without Caddy](#without-caddy) until the ports are free.
- **They become open to the network.** Docker publishes ports past host firewalls such as `ufw`, so a host that exposed only `44330`/`44333` now also answers on `80` and `443`. If the server must not be reachable on these ports, use `self-signed` and remove the `80:80` and `443:443` lines from the `caddy` service.

Then replace your `docker-compose.yml` with the reference one, create the `.env` file (step 2 above), and restart:

```bash
docker compose down
docker compose up -d
```

Check the certificate (step 5 above). What happens to existing clients:

- **Clients that use `HSM_DOMAIN`** (`https://hsm.example.com:44330`) get the new certificate. With `letsencrypt` it is trusted, so for them you can switch off "Allow untrusted server certificate".
- **Clients that use any other address**, such as the server's IP or a short internal name, keep working, including already distributed agent bundles. They receive the `HSM_DOMAIN` certificate, which does not match their address, so they must keep "allow untrusted certificate" on. To give them the trusted certificate, switch them to the `HSM_DOMAIN` address. For agents, set **Agent connection URL** (Configuration → Agent) to `https://<HSM_DOMAIN>:44330` and redistribute the bundles.

---

## Method 2 — docker run (manual)

**1. Pull the image:**

```bash
docker pull hsmonitoring/hierarchical_sensor_monitoring:latest
```

**2. Run the container.** Without Caddy in front, HSM serves HTTPS itself with a built-in self-signed certificate (or your own, see [Server Configuration](Server-Configuration)).

```bash
docker run -u 0 -d \
  --restart unless-stopped \
  -v /host/path/Logs:/app/Logs \
  -v /host/path/Config:/app/Config \
  -v /host/path/Databases:/app/Databases \
  -v /host/path/DatabasesBackups:/app/DatabasesBackups \
  -p 44330:44330 \
  -p 44333:44333 \
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

Replace `/host/path/` with an actual directory on your machine.

**Windows example:**

```bash
docker run -u 0 -d ^
  --restart unless-stopped ^
  -v C:\HSM\Logs:/app/Logs ^
  -v C:\HSM\Config:/app/Config ^
  -v C:\HSM\Databases:/app/Databases ^
  -v C:\HSM\DatabasesBackups:/app/DatabasesBackups ^
  -p 44330:44330 ^
  -p 44333:44333 ^
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

---

## Method 3 — Script

Ready-made scripts are available in the repository:

**PowerShell (Windows):**

Download and run [`docker_scripts/HSMserver/server_load.ps1`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker_scripts/HSMserver/server_load.ps1)

```powershell
.\server_load.ps1
```

**Bash (Linux/macOS):**

Download and run [`docker_scripts/HSMserver/load.sh`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker_scripts/HSMserver/load.sh)

```bash
chmod +x load.sh && ./load.sh
```

The scripts pull the latest image and start the container with the correct volume and port mappings.

---

## Volume Mounts — Important

Always mount these directories. Without them, **all data is lost** when the container is stopped or updated:

| Host path | Container path | Contents |
|---|---|---|
| `./Logs` | `/app/Logs` | Application logs |
| `./Config` | `/app/Config` | `appsettings.json`, TLS certificates |
| `./CaddyData` | `/data` (caddy) | Caddy's certificates and Let's Encrypt account (compose only) |
| `./Databases` | `/app/Databases` | All sensor data (LevelDB) |
| `./DatabasesBackups` | `/app/DatabasesBackups` | Automatic database backups |

> The `Databases` volume is the most critical — it contains all sensor history and configuration. Never remove it without a backup.

---

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| `44330` | HTTPS | Sensor data ingestion — used by DataCollector and REST API |
| `44333` | HTTPS | Web UI |
| `443` | HTTPS | Web UI on the standard port (compose only; can be removed for IP-address setups) |
| `80` | HTTP | Let's Encrypt domain check and redirect to HTTPS (compose only; can be removed for IP-address setups) |

With Docker Compose, HSM is reachable only through Caddy, and Caddy publishes these ports. Keep `44330` and `44333`: downloaded agent bundles point at port `44330`. If you must use other host ports, change the left side of the mapping in the `caddy` service and set **Agent connection URL** (Configuration → Agent) to the new Sensor API address, e.g. `https://hsm.example.com:44331`.

For `docker run`, change the mapping and `Config/appsettings.json`:

```yaml
ports:
  - '44331:44330'   # host port : container port
  - '44334:44333'
```

```json
"Kestrel": {
  "SensorPort": 44330,
  "SitePort": 44333
}
```

Note: the `Kestrel` config defines the ports the server listens on inside the container. The left side of the Docker port mapping is what you expose on the host.

---

## Updating

To update to the latest version:

```bash
# Pull the new images (HSM and Caddy)
docker compose pull

# Restart with Docker Compose (data is preserved in volumes)
docker compose down
docker compose up -d
```

Without Caddy, add `-f docker-compose.direct.yml` to each `docker compose` command. With `docker run`, stop and remove the old container, then run the `docker run` command again. Volumes are not affected.

---

## First Login

After starting the server, open `https://<HSM_DOMAIN>:44333` (compose) or `https://localhost:44333` (`docker run`).

- Username: `default`
- Password: `default`

Go to **Account → Settings** and change the password before doing anything else.

---

## Next Steps

- [Server Configuration](Server-Configuration) — ports, TLS, backups, Telegram bot
- [Getting Started](Getting-Started) — create a product and send your first sensor value
