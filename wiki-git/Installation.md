# Installation

HSM Server is distributed as a Docker image. This page covers all deployment methods.

---

## Prerequisites

- [Docker](https://www.docker.com/) with Docker Compose v2.23.1 or newer
- Ports `44330` and `44333` available on the host, plus `80` and `443` for the automatic certificate. Check that nothing else on the host (another web server or proxy) already uses `80`/`443`, otherwise Caddy cannot start. With `HSM_DOMAIN` set to an IP address, `80`/`443` are not needed and you can remove those two lines from the `caddy` service.

---

## Method 1 — Docker Compose (recommended)

The compose file runs two containers: the HSM server and [Caddy](https://caddyserver.com/), a web server in front of it. Caddy takes care of the certificate clients see: it gets one, renews it before it expires, and passes requests to HSM. You do not create, choose or install any certificate yourself.

**1. Download the reference compose file** (full text below, in [Reference docker-compose.yml](#reference-docker-composeyml)):

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
```

Do not write your own compose file or put a different proxy in front: the HSM side of the setup depends on exactly this Caddy configuration. The edits described on this page (`tls internal`, removing ports `80`/`443`, an e-mail for Let's Encrypt) are fine.

**2. Tell Caddy the server address.** Create a file named `.env` next to `docker-compose.yml`. It is required: without it every `docker compose` command (`up`, `down`, `logs`, `pull`) stops with an error, so keep the file next to the compose file. If it is lost, you can still stop the stack with `HSM_DOMAIN=x docker compose down`.

```dotenv
HSM_DOMAIN=hsm.example.com
```

Write a bare host name or IP address only: no `https://`, no port, no trailing slash.

What you put there decides which certificate Caddy uses:

| `HSM_DOMAIN` | Certificate |
|---|---|
| Public DNS name, e.g. `hsm.example.com` | Free trusted certificate from **Let's Encrypt**, renewed automatically. The DNS record must point to this machine, and ports `80`/`443` must be reachable from the internet. |
| IP address, e.g. `10.0.0.5` | Caddy's own self-signed certificate: browsers show a warning, and collectors/agents need "allow untrusted certificate". |

**3. Start the server:**

```bash
docker compose up -d
```

**4. Open the web UI:** `https://<HSM_DOMAIN>` or `https://<HSM_DOMAIN>:44333`.

Default credentials: login `default`, password `default`. **Change the password immediately.**

Collectors and agents connect to `https://<HSM_DOMAIN>:44330`, as before.

> If the certificate does not appear, check `docker logs hsm-caddy`. The usual cause is that the DNS record does not point to the server yet, or port 80 is closed in the firewall.

Optionally, give Let's Encrypt an e-mail for expiry warnings and account recovery: add `email admin@example.com` as the first line inside the leading `{ ... }` block of the `caddyfile` section.

### If Caddy does not start

HSM itself publishes no ports, so while Caddy is down (a port conflict, a typo in `HSM_DOMAIN`, a broken edit of the `caddyfile` section) the web UI is unreachable too. Check `docker logs hsm-caddy`. To reach HSM directly in the meantime, create `docker-compose.recovery.yml` next to the compose file:

```yaml
services:
  app:
    ports: ['127.0.0.1:44333:44333']
```

and start only HSM with it:

```bash
docker compose -f docker-compose.yml -f docker-compose.recovery.yml up -d app
```

The web UI is then at `https://localhost:44333` on the server itself, with HSM's own certificate. Run `docker compose up -d` again once Caddy is fixed.

### Reference docker-compose.yml

This is the supported setup, the same file as [`docker-compose.yml`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker-compose.yml) in the repository:

```yaml
# HSM Server behind Caddy. Everyone runs the same stack with `docker compose up -d`.
#
# Caddy terminates TLS for clients and obtains/renews the certificate by itself. HSM keeps
# serving HTTPS with its own (self-signed by default) certificate, reachable only inside the
# compose network; Caddy connects to it without verifying that certificate.
# Set the address clients use in a `.env` file next to this one (or in the shell); compose
# refuses to start without it:
#   HSM_DOMAIN=hsm.example.com   public DNS name -> Let's Encrypt certificate
#                                (DNS must point here, port 80 reachable from the internet)
#   HSM_DOMAIN=10.0.0.5          IP address -> Caddy's own self-signed certificate
# Collectors and agents keep using https://<HSM_DOMAIN>:44330.
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
      - app
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
      - ./Logs/caddy:/var/log/caddy            # access log of every request, with client IPs

configs:
  caddyfile:
    content: |
      {
          # Clients that reach this host by IP (no SNI) or by another name get the
          # HSM_DOMAIN certificate instead of a refused handshake.
          default_sni ${HSM_DOMAIN:?Set HSM_DOMAIN in .env next to docker-compose.yml - see the comment at the top}
          fallback_sni ${HSM_DOMAIN}
      }
      (hsm_upstream) {
          transport http {
              tls_insecure_skip_verify
          }
      }
      (hsm_access_log) {
          log {
              output file /var/log/caddy/access.log {
                  roll_size 50MiB
                  roll_keep 10
              }
          }
      }
      ${HSM_DOMAIN}, ${HSM_DOMAIN}:44333 {
          import hsm_access_log
          reverse_proxy https://app:44333 {
              import hsm_upstream
          }
      }
      ${HSM_DOMAIN}:44330 {
          import hsm_access_log
          reverse_proxy https://app:44330 {
              import hsm_upstream
          }
      }
      # Any other address of this host (IP, short name, old CNAME): existing collectors and
      # agent bundles keep working. The certificate does not match such a name, so those
      # clients need "allow untrusted certificate", as with the old self-signed setup.
      https://:443, https://:44333 {
          import hsm_access_log
          reverse_proxy https://app:44333 {
              import hsm_upstream
          }
      }
      https://:44330 {
          import hsm_access_log
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
| `default_sni` / `fallback_sni` and the `https://:443`, `https://:44333`, `https://:44330` sites | Clients that reach the server by IP or by another name keep working. They receive the `HSM_DOMAIN` certificate, which does not match the name they use, so they need "allow untrusted certificate". |
| Public ports `44330` and `44333` | Collectors and agents connect to `https://<host>:44330`; downloaded agent bundles use this port. |
| Ports `80` and `443` | Required when `HSM_DOMAIN` is a public DNS name: Let's Encrypt checks the domain through them. With an IP address or `tls internal` you can remove both lines; the web UI is then available on `44333` only. |
| `./CaddyData:/data` | Keeps certificates across updates; without it Caddy requests new ones on every restart and hits Let's Encrypt rate limits. |
| `Kestrel__TrustedProxies__0: 'attached-networks'` | HSM trusts the client address that Caddy forwards only from the network of its own container, the compose network, whose only other member is Caddy. The web UI and the API-token audit then show the real client IP, and nobody else can choose that address. No subnet has to be picked, so it cannot collide with other Docker networks on the host. |
| `./Logs/caddy` access log | Every request with its client IP address (`Logs/caddy/access.log`), useful for incident analysis. |
| `caddy:2.11.4` (pinned version) | Clients on other addresses depend on how Caddy picks a certificate; a new Caddy version is taken deliberately, after checking that behavior again. |

### Internal DNS name (not reachable from the internet)

Let's Encrypt cannot issue a certificate for a name it cannot reach. Either set `HSM_DOMAIN` to the server's IP address, or add `tls internal` to both `HSM_DOMAIN` sites in the `caddyfile` section of `docker-compose.yml`:

```
hsm.corp.lan, hsm.corp.lan:44333 {
    tls internal
    reverse_proxy https://app:44333 {
        import hsm_upstream
    }
}
```

Either way the certificate comes from Caddy's own certificate authority, which clients do not trust: browsers show a warning, and collectors/agents need "allow untrusted certificate". To avoid that, install Caddy's root certificate on the client machines. It is `CaddyData/caddy/pki/authorities/local/root.crt` next to the compose file.

### Moving an existing installation to Caddy

Nothing changes on the HSM side: it keeps its data, settings and own certificate.

**Before you stop the old stack**, check ports `80` and `443`:

- **They must be free on the host.** If they are taken, the new stack fails to start after the old one is already down.
- **They become open to the network.** Docker publishes ports past host firewalls such as `ufw`, so a host that exposed only `44330`/`44333` now also answers on `80` and `443`. If the server must not be reachable on these ports, remove the `80:80` and `443:443` lines from the `caddy` service before starting. Let's Encrypt cannot work then:
  - with an IP address in `HSM_DOMAIN` nothing else is needed; Caddy uses its own certificate;
  - with a DNS name in `HSM_DOMAIN` you **must** also add `tls internal` to both `HSM_DOMAIN` sites (see "Internal DNS name" above). Otherwise Caddy never gets a certificate, and every TLS connection fails: browsers and collectors on `44330` alike.

Then replace your `docker-compose.yml` with the reference one, create the `.env` file (step 2 above), and restart:

```bash
docker compose down
docker compose up -d
```

What happens to existing clients:

- **Clients that use `HSM_DOMAIN`** (`https://hsm.example.com:44330`) get the new trusted certificate. For them you can switch off "Allow untrusted server certificate".
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
| `./Logs/caddy` | `/var/log/caddy` (caddy) | Access log with real client IP addresses (compose only) |
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

Or with `docker run` — stop and remove the old container, then run the `docker run` command again. Volumes are not affected.

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
