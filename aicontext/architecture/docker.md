# Docker Setup

> Owner: shared | Last reviewed: 2026-09-22 | Canonical: yes

## Production Deployment

Single container via `docker-compose.yml`:

```yaml
services:
  app:
    image: 'hsmonitoring/hierarchical_sensor_monitoring:latest'
    restart: unless-stopped
    user: '0'
    ports:
      - '44330:44330'   # Sensor API (DataCollector sends data here)
      - '44333:44333'   # Web UI (admin dashboard)
    volumes:
      - ./Logs:/app/Logs
      - ./Config:/app/Config
      - ./Databases:/app/Databases
      - ./DatabasesBackups:/app/DatabasesBackups
```

## Ports

| Port | Purpose |
|---|---|
| 44330 | Sensor API — DataCollector sends values here (`/api/sensors/*`) |
| 44333 | Web UI — browser dashboard, user management, alerts |

## Volumes

| Volume | Purpose |
|---|---|
| `Logs` | NLog output files |
| `Config` | Server configuration (TLS, Telegram, backup settings) |
| `Databases` | LevelDB data files (sensor history, metadata) |
| `DatabasesBackups` | Automated SFTP backup snapshots |

## TLS: built-in certificate or reverse proxy (#1411)

By default Kestrel serves HTTPS on both ports with `Config/<ServerCertificate.Name>` (PFX, optional `Key` password) or, when that file is absent, the bundled self-signed `default.server.pfx`. The certificate is loaded once at startup, so a renewed PFX needs a restart.

For an automatically renewed public certificate (Let's Encrypt), put a TLS-terminating reverse proxy in front and switch HSM to plain HTTP in `Config/appsettings.json`:

```json
"Kestrel": { "SensorPort": 44330, "SitePort": 44333, "UseHttps": false, "TrustedProxies": [] }
```

With `UseHttps: false`:

- both ports listen on plain HTTP/1.1, and no certificate is loaded;
- HSTS and the HTTPS redirect are off, because the proxy owns them;
- `X-Forwarded-Proto` / `X-Forwarded-For` are honoured **only** from `TrustedProxies` (IP or CIDR). An empty list means loopback plus private networks (`10/8`, `172.16/12` including Docker bridges, `192.168/16`, `fc00::/7`). The client's `https` scheme is therefore restored for cookies (`Secure`), for the agent-bundle address (`AgentConnectionResolver`) and for the token-audit IPs, and a direct public client cannot spoof it;
- a malformed `TrustedProxies` entry fails startup with the key named; startup logs a warning that a proxy is expected.

**Never publish the HSM ports directly in this mode.** Only the proxy may be reachable. Keep the public ports equal to `SensorPort`/`SitePort`: the UI/Sensor-API split is by listener port, and the agent bundle falls back to `SensorPort`. Otherwise set the agent's External connection URL.

Caddy example: it obtains and renews the certificate itself; ports 80/443 must be reachable for the ACME challenge.

```
{
    email admin@example.com
}
hsm.example.com, hsm.example.com:44333 {
    reverse_proxy http://hsm-server:44333
}
hsm.example.com:44330 {
    reverse_proxy http://hsm-server:44330
}
```

```yaml
  app:            # no `ports:` — reachable only through caddy
    ...
  caddy:
    image: caddy:2
    restart: unless-stopped
    ports: ['80:80', '443:443', '44330:44330', '44333:44333']
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - ./caddy_data:/data   # ACME account + certificates; keep it
```

## Notes

- No external database service needed — LevelDB is embedded
- Server listens on both ports via Kestrel multi-binding
- TLS certificate is configured in `Config/` volume, or terminated by a reverse proxy with `Kestrel.UseHttps: false` (see above)

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
