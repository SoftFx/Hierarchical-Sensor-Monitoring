# Installation

HSM Server is distributed as a Docker image. This page covers all deployment methods.

---

## Prerequisites

- [Docker](https://www.docker.com/) with Docker Compose v2.23 or newer
- Ports `44330` and `44333` available on the host, plus `80` and `443` for the automatic certificate

---

## Method 1 — Docker Compose (recommended)

The compose file runs two containers: the HSM server and [Caddy](https://caddyserver.com/), a web server in front of it. Caddy takes care of the certificate clients see: it gets one, renews it before it expires, and passes requests to HSM. You do not create, choose or install any certificate yourself.

**1. Download the reference compose file** (full text below, in [Reference docker-compose.yml](#reference-docker-composeyml)):

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
```

Use this file as is. Do not write your own compose file or put a different proxy in front: the HSM side of the setup depends on exactly this Caddy configuration.

**2. Tell Caddy the server address.** Create a file named `.env` next to `docker-compose.yml`. It is required: without it `docker compose up` stops with an error.

```bash
HSM_DOMAIN=hsm.example.com
```

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
    volumes:
      - ./Logs:/app/Logs                       # NLog output
      - ./Config:/app/Config                   # server config (TLS cert, Telegram, Agent settings)
      - ./Databases:/app/Databases             # embedded LevelDB (sensor history + metadata)
      - ./DatabasesBackups:/app/DatabasesBackups

  caddy:
    image: 'caddy:2'
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

configs:
  caddyfile:
    content: |
      (hsm_upstream) {
          transport http {
              tls_insecure_skip_verify
          }
      }
      ${HSM_DOMAIN:?Set HSM_DOMAIN in .env next to docker-compose.yml - see the comment at the top}, ${HSM_DOMAIN}:44333 {
          reverse_proxy https://app:44333 {
              import hsm_upstream
          }
      }
      ${HSM_DOMAIN}:44330 {
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
| Public ports `44330` and `44333` | Collectors and agents connect to `https://<host>:44330`; downloaded agent bundles use this port. |
| Ports `80` and `443` | Let's Encrypt checks the domain through them. |
| `./CaddyData:/data` | Keeps certificates across updates; without it Caddy requests new ones on every restart and hits Let's Encrypt rate limits. |

### Internal DNS name (not reachable from the internet)

Let's Encrypt cannot issue a certificate for a name it cannot reach. Either set `HSM_DOMAIN` to the server's IP address, or add `tls internal` to both sites in the `caddyfile` section of `docker-compose.yml`:

```
hsm.corp.lan, hsm.corp.lan:44333 {
    tls internal
    reverse_proxy https://app:44333 {
        import hsm_upstream
    }
}
```

### Moving an existing installation to Caddy

Nothing changes on the HSM side: it keeps its data, settings and own certificate. Replace your `docker-compose.yml` with the reference one, create the `.env` file (step 2 above), then:

```bash
docker compose down
docker compose up -d
```

Collector and agent addresses stay the same (`https://<host>:44330`). Once the certificate is trusted, you can switch off "Allow untrusted server certificate" in the agent settings.

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
| `443` | HTTPS | Web UI on the standard port (compose only) |
| `80` | HTTP | Let's Encrypt domain check and redirect to HTTPS (compose only) |

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
