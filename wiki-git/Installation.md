# Installation

HSM Server is distributed as a Docker image. This page covers all deployment methods.

---

## Prerequisites

- [Docker](https://www.docker.com/) with Docker Compose v2.23 or newer
- Ports `44330` and `44333` available on the host, plus `80` and `443` for the automatic certificate

---

## Method 1 — Docker Compose (recommended)

The compose file runs two containers: the HSM server and [Caddy](https://caddyserver.com/), a web server in front of it. Caddy takes care of HTTPS: it gets a certificate, renews it before it expires, and passes requests to HSM. You do not create, choose or install any certificate yourself.

**1. Download the compose file:**

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
```

**2. Tell Caddy the server address.** Create a file named `.env` next to `docker-compose.yml`:

```bash
HSM_DOMAIN=hsm.example.com
```

What you put there decides which certificate Caddy uses:

| `HSM_DOMAIN` | Certificate |
|---|---|
| Public DNS name, e.g. `hsm.example.com` | Free trusted certificate from **Let's Encrypt**, renewed automatically. The DNS record must point to this machine, and ports `80`/`443` must be reachable from the internet. |
| IP address, e.g. `10.0.0.5`, or `localhost` | Caddy's own self-signed certificate: browsers show a warning, and collectors/agents need "allow untrusted certificate". |
| not set | `localhost`: only usable from the same machine. |

**3. Start the server:**

```bash
docker compose up -d
```

**4. Open the web UI:** `https://<HSM_DOMAIN>` or `https://<HSM_DOMAIN>:44333`.

Default credentials: login `default`, password `default`. **Change the password immediately.**

Collectors and agents connect to `https://<HSM_DOMAIN>:44330`, as before.

> If the certificate does not appear, check `docker logs hsm-caddy`. The usual cause is that the DNS record does not point to the server yet, or port 80 is closed in the firewall.

### Internal DNS name (not reachable from the internet)

Let's Encrypt cannot issue a certificate for a name it cannot reach. Either set `HSM_DOMAIN` to the server's IP address, or add `tls internal` to both sites in the `caddyfile` section of `docker-compose.yml`:

```
hsm.corp.lan, hsm.corp.lan:44333 {
    tls internal
    reverse_proxy app:44333
}
```

### Moving an existing installation to Caddy

An installation that already ran HSM keeps serving HTTPS with its own certificate after an update, so nothing breaks. The new `docker-compose.yml` switches HSM to plain HTTP behind Caddy with its `Kestrel__UseHttps: 'false'` setting, which works as long as `Config/appsettings.json` has no `UseHttps` key. If it has `"UseHttps": true` (written by a newer server started without the new compose file), change it to `false`. Then:

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

**2. Run the container.** Without Caddy in front, HSM serves HTTPS itself with a built-in self-signed certificate: keep `-e Kestrel__UseHttps=true`.

```bash
docker run -u 0 -d \
  --restart unless-stopped \
  -e Kestrel__UseHttps=true \
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
  -e Kestrel__UseHttps=true ^
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

Always mount these four directories. Without them, **all data is lost** when the container is stopped or updated:

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

With Docker Compose, HSM is reachable only through Caddy, and Caddy publishes these ports. To use other host ports, change the left side of the mapping in the `caddy` service. For `docker run`, change the mapping and `Config/appsettings.json`:

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
# Pull the new image
docker pull hsmonitoring/hierarchical_sensor_monitoring:latest

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
