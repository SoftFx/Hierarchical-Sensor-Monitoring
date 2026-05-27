# Docker Setup

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

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

## Notes

- No external database service needed — LevelDB is embedded
- Server listens on both ports via Kestrel multi-binding
- TLS certificate is configured in `Config/` volume
