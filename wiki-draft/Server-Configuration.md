# Server Configuration

HSM Server is configured via `Config/appsettings.json` (in release builds) located next to the server executable, or inside the Docker container at the same path.

Most settings can also be changed through the **Configuration** page in the web UI without restarting the server.

---

## Ports

```json
"Kestrel": {
  "SensorPort": 44330,
  "SitePort": 44333
}
```

| Setting | Default | Description |
|---|---|---|
| `SensorPort` | `44330` | Port for sensor data ingestion (used by DataCollector and REST API) |
| `SitePort` | `44333` | Port for the web UI |

Both ports use **HTTPS**. To change them, edit `appsettings.json` and restart the server, or update via **Configuration → Server** in the web UI.

In Docker, map these ports when running the container:
```bash
docker run -p 44330:44330 -p 44333:44333 ...
```

---

## TLS Certificate

With the Docker Compose setup, you do not need anything here: Caddy obtains and renews the certificate clients see (Let's Encrypt for a public domain), see [Installation](Installation). The settings below matter when clients connect to HSM directly (`docker run`, no Caddy):

```json
"ServerCertificate": {
  "Name": "",
  "Key": ""
}
```

By default, HSM uses a built-in self-signed certificate. To use your own `.pfx`:

1. Put the `.pfx` file into the `Config` folder (`/app/Config` in Docker)
2. Set `Name` to the file name, e.g. `hsm.pfx`
3. Set `Key` to the certificate password (leave empty if there is none)
4. Restart the server; the certificate is read only at startup

---

## Database Backups

```json
"BackupDatabase": {
  "IsEnabled": true,
  "PeriodHours": 1,
  "StoragePeriodDays": 10,
  "SftpConnectionConfig": { ... }
}
```

| Setting | Default | Description |
|---|---|---|
| `IsEnabled` | `true` | Enable/disable automatic backups |
| `PeriodHours` | `1` | How often to create a backup (in hours) |
| `StoragePeriodDays` | `10` | How long to keep old backups (in days) |
| `SftpConnectionConfig` | — | Optional SFTP upload target |

Backups are stored in the `DatabasesBackups/` directory. When running in Docker, mount this directory to a host path to keep backups outside the container.

**SFTP upload** — configure to automatically push backups to a remote server:
```json
"SftpConnectionConfig": {
  "Host": "backup.example.com",
  "Port": 22,
  "Username": "backup_user",
  "Password": "...",
  "TargetPath": "/backups/hsm"
}
```

---

## Telegram Bot

```json
"Telegram": {
  "BotName": "@YourBotName",
  "BotToken": "123456789:AAF...",
  "IsRunning": false
}
```

| Setting | Description |
|---|---|
| `BotName` | Telegram bot username (with or without `@`) |
| `BotToken` | Token from [@BotFather](https://t.me/botfather) |
| `IsRunning` | Whether the bot should start automatically on server launch |

You can start/stop the bot from **Configuration → Telegram** in the web UI without editing the file.

See [Telegram Setup](Telegram-Setup) for the full setup guide.

---

## Docker Volume Mounts

When running with Docker, mount these directories to preserve data across container restarts:

```bash
docker run \
  -v /host/Logs:/app/Logs \
  -v /host/Config:/app/Config \
  -v /host/Databases:/app/Databases \
  -v /host/DatabasesBackups:/app/DatabasesBackups \
  -p 44330:44330 \
  -p 44333:44333 \
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

With Docker Compose, use the reference `docker-compose.yml` from [Installation](Installation) instead of writing your own: it already mounts these directories.

| Directory | Contents |
|---|---|
| `Logs/` | Application logs |
| `Config/` | `appsettings.json` and certificates |
| `Databases/` | LevelDB sensor data and metadata |
| `DatabasesBackups/` | Automatic database backups |

---

## Default Login

After first launch, log in with:
- Username: `default`
- Password: `default`

**Change the password immediately** via **Account → Settings** in the web UI.

---

## Server Monitoring

HSM monitors itself and exposes internal metrics as sensors under a dedicated product. You can view server health, request counts, Telegram bot statistics, and more directly in the sensor tree.

See [HSM Server Monitoring](HSM-Server-Monitoring) for details.
