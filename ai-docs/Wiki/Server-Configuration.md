# Server Configuration

HSM Server is configured via `Config/appsettings.json`, located next to the server executable. In Docker, this file is inside the container at the same relative path — mount the `Config/` directory to persist it.

Most settings can also be changed at runtime through **Configuration** in the web UI without restarting the server.

---

## Full appsettings.json Example

```json
{
  "Kestrel": {
    "SensorPort": 44330,
    "SitePort": 44333
  },
  "ServerCertificate": {
    "CertificatePath": "",
    "CertificatePassword": ""
  },
  "BackupDatabase": {
    "IsEnabled": true,
    "PeriodHours": 1,
    "StoragePeriodDays": 10,
    "SftpConnectionConfig": {
      "IsEnabled": false,
      "Address": "",
      "Port": 22,
      "Username": "",
      "Password": "",
      "PrivateKeyFileName": "",
      "PrivateKey": "",
      "RootPath": ""
    }
  },
  "Telegram": {
    "BotName": "",
    "BotToken": "",
    "IsRunning": false
  }
}
```

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
| `SensorPort` | `44330` | HTTPS port for sensor data ingestion (DataCollector and REST API) |
| `SitePort` | `44333` | HTTPS port for the web UI |

To change ports, update `appsettings.json` and restart the server, or go to **Configuration → Server** in the web UI.

In Docker — map these ports when running the container:
```bash
docker run -p 44330:44330 -p 44333:44333 ...
```

---

## TLS Certificate

```json
"ServerCertificate": {
  "CertificatePath": "",
  "CertificatePassword": ""
}
```

By default, HSM generates and uses a **self-signed certificate**. To use your own `.pfx` certificate:

1. Set `CertificatePath` — absolute path to the `.pfx` file
2. Set `CertificatePassword` — password for the certificate
3. Restart the server

In Docker, mount the certificate file into the container:
```bash
docker run \
  -v /host/certs/hsm.pfx:/app/Config/hsm.pfx \
  ...
```
Then set `CertificatePath` to `/app/Config/hsm.pfx`.

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
| `IsEnabled` | `true` | Enable automatic periodic backups |
| `PeriodHours` | `1` | How often to create a backup, in hours |
| `StoragePeriodDays` | `10` | How long to keep local backups before deleting old ones |

**What is backed up:** The `EnvironmentData` and `ServerLayout` databases — structure metadata, users, products, sensors, dashboards. Sensor history (`History` database) is **not** included in backups due to size.

Backups are stored in the `DatabasesBackups/` directory. In Docker, mount this directory to keep backups on the host:
```bash
docker run -v /host/DatabasesBackups:/app/DatabasesBackups ...
```

### Backup Monitoring

The backup status is visible in the **HSM Server Monitoring** product under the `Backup/` node:

| Sensor | Description |
|---|---|
| `Backup/Local backup size` | Size of the latest local backup (MB). Status = Error if backup failed |
| `Backup/Remote backup size` | Size of the latest SFTP upload (MB). Status = Error if upload failed |

Set a TTL alert on these sensors to get notified if backups stop being created.

### SFTP Upload

Backups can be automatically uploaded to a remote server via SFTP after each local backup is created.

```json
"SftpConnectionConfig": {
  "IsEnabled": true,
  "Address": "backup.example.com",
  "Port": 22,
  "Username": "hsm_backup",
  "Password": "your_password",
  "PrivateKeyFileName": "",
  "PrivateKey": "",
  "RootPath": "/backups/hsm"
}
```

| Setting | Description |
|---|---|
| `IsEnabled` | Enable SFTP upload |
| `Address` | SFTP server hostname or IP |
| `Port` | SFTP port (default: `22`) |
| `Username` | SFTP username |
| `Password` | Password authentication (leave empty if using key) |
| `PrivateKeyFileName` | Path to the private key file (`.pem` or OpenSSH format) |
| `PrivateKey` | Private key content as a string (alternative to `PrivateKeyFileName`) |
| `RootPath` | Remote directory where backups are uploaded |

Use either `Password` or `PrivateKey`/`PrivateKeyFileName` for authentication — not both.

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
| `IsRunning` | If `true`, bot starts automatically when the server launches |

You can also start/stop the bot at runtime from **Configuration → Telegram** in the web UI without editing the file.

For the full Telegram setup guide, see [Telegram Setup](Telegram-Setup).

---

## Docker Volume Mounts

Always mount these directories to preserve data across container restarts and updates:

```bash
docker run \
  -v /host/Logs:/app/Logs \
  -v /host/Config:/app/Config \
  -v /host/Databases:/app/Databases \
  -v /host/DatabasesBackups:/app/DatabasesBackups \
  -p 44330:44330 \
  -p 44333:44333 \
  softfx/hsm-server:latest
```

Or in `docker-compose.yml`:
```yaml
services:
  hsm:
    image: softfx/hsm-server:latest
    ports:
      - "44330:44330"
      - "44333:44333"
    volumes:
      - ./Logs:/app/Logs
      - ./Config:/app/Config
      - ./Databases:/app/Databases
      - ./DatabasesBackups:/app/DatabasesBackups
```

| Directory | Contents |
|---|---|
| `Logs/` | Application logs |
| `Config/` | `appsettings.json` and TLS certificate |
| `Databases/` | LevelDB databases (sensor history, metadata, dashboards) |
| `DatabasesBackups/` | Automatic database backups |

> If you do not mount `Databases/`, all sensor data is lost when the container is removed or updated.

---

## Default Login

After first launch:
- Username: `default`
- Password: `default`

**Change the password immediately** via **Account → Settings** in the web UI.

---

## Server Self-Monitoring

HSM collects metrics about its own performance into a dedicated product called **HSM Server Monitoring**. This includes database sizes, API traffic, backup status, and Telegram bot statistics.

See [HSM Server Monitoring](HSM-Server-Monitoring) for details.

---

## See Also

- [Home](Home) — Main documentation entry point
- [Installation](Installation) — How to deploy the HSM server
- [HSM Server Monitoring](HSM-Server-Monitoring) — Self-monitoring sensors
- [Telegram Setup](Telegram-Setup) — Step-by-step Telegram bot setup
- [Configuration](Configuration) — Admin configuration in the web UI
