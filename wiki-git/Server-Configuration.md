# Server Configuration

HSM Server is configured via `Config/appsettings.json`, located next to the server executable. In Docker, this file is inside the container at the same relative path — mount the `Config/` directory to persist it.

Most settings can also be changed at runtime through **Configuration** in the web UI without restarting the server.

---

## Full appsettings.json Example

```json
{
  "Kestrel": {
    "Port": 443
  },
  "ServerCertificate": {
    "Name": "",
    "Key": ""
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
  "Port": 443
}
```

| Setting | Default | Description |
|---|---|---|
| `Port` | `443` | The single HTTPS port the server listens on — web UI, sensor data ingestion, management API (#1590). Pre-#1590 `SensorPort`/`SitePort` keys in an existing file are ignored with a startup warning and removed on the first start |

With the [Docker Compose setup](Installation), keep both at their defaults: Caddy forwards to exactly these ports.

`Kestrel:TrustedProxies` is a list of reverse-proxy addresses (IP, CIDR such as `172.18.0.0/16`, or `attached-networks` for the networks of the server's own interfaces) whose `X-Forwarded-For` header HSM trusts for the client address. It is empty by default: HSM then records the address of whoever connects to it. The Docker Compose setup fills it in for its own Caddy; you do not need to set it yourself. It is read **from environment variables only**, as `Kestrel__TrustedProxies=172.18.0.0/16,10.1.0.5` or `Kestrel__TrustedProxies__0=...`, `Kestrel__TrustedProxies__1=...`. The server rewrites `appsettings.json` on every start, so a value there could not be kept: it is ignored, **removed from the file**, and reported once as an error in the log. When HSM runs as a service behind your own proxy, set the variable for that service. An empty value switches the trust off.

> **Warning:** `attached-networks` trusts every network the server's own interfaces are on. In the Docker Compose setup that is the private compose network. With `network_mode: host`, a macvlan network, or HSM installed directly on a machine, it means **your whole LAN**: any machine there could then choose the client address HSM records. Outside the Compose setup, list your proxy's exact address instead.

To change ports, update `appsettings.json` and restart the server, or go to **Configuration → Server** in the web UI.

In Docker — map these ports when running the container:
```bash
docker run -p 443:443 ...
```

---

## TLS Certificate

With the Docker Compose setup, you do not need anything here: Caddy obtains and renews the certificate clients see (Let's Encrypt for a public domain), see [Installation](Installation). HSM's own certificate is then used only between Caddy and HSM.

The settings below matter when clients connect to HSM directly (`docker run`, no Caddy):

```json
"ServerCertificate": {
  "Name": "",
  "Key": ""
}
```

By default, HSM uses a built-in **self-signed certificate**. To use your own `.pfx` certificate:

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

## Sealed Sensor History

```json
"HistorySealing": {
  "Enabled": false,
  "SealAfterDays": 7,
  "KeepSourceRowsWeeks": 4,
  "WorkingHoursStartUtc": "00:00",
  "WorkingHoursEndUtc": "00:00",
  "FoldMinRows": 10000,
  "FoldQuietHours": 24
}
```

A background job can store closed weeks of sensor history in a compact form (about 7.5 bytes per value instead of about 43 after compaction), while every read keeps returning the same values (`ReceivingTime` is kept to the millisecond). It is **off by default**. Sensor history is not part of the backups, so the job verifies everything it writes and keeps the original rows for a while.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Turns the job on. When off it does nothing; weeks already sealed stay readable. |
| `SealAfterDays` | `7` | A week (Monday to Monday, UTC) is sealed this many days after it ended (1 to 3650). |
| `KeepSourceRowsWeeks` | `4` | After a verified seal, the original rows stay this many weeks before they are removed (1 to 520). The week takes more space until then. The time is fixed per week when it is sealed. |
| `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `00:00`, `00:00` | The daily UTC window the job works in, `HH:mm`. Equal values mean all day; `22:00` to `06:00` wraps past midnight. |
| `FoldMinRows` | `10000` | Values written into an already sealed week (late values, edits) are folded into it once this many have collected... |
| `FoldQuietHours` | `24` | ...or once the week has had no new write for this many hours. |

The settings are read when the server starts. An invalid value stops the start with an error naming the key.

**Unseal.** **Configuration → Database**, select a week in *Values Database* and press **Unseal** (administrators only) to turn a sealed week back into ordinary rows. The job then leaves that week alone until the file `hsm-unsealed` is deleted from the week's `SensorValuesV2_*` directory. Unseal every sealed week before rolling the server back to a version without sealed history: an older server cannot see sealed weeks.

To cancel an unseal that cannot finish (a damaged chunk), delete the file `hsm-unsealing` from the week's `SensorValuesSealedV1_*` directory.

The job reports itself under **HSM Server Monitoring → Database → Sealed history** (last run, sealed weeks, weeks needing attention, rows and bytes sealed, sensors left as rows, verification failures, chunk read, rewrite and directory sync failures). **Weeks needing attention** above 0 means the job has stopped touching a week until you act; its comment says which week and what to do. Never delete a `SensorValuesSealedV1_*` directory by hand unless that comment says its rows are complete: after the keep period it holds the only copy of the week.

### History Compaction

```json
"HistoryCompaction": {
  "Enabled": false,
  "WorkingHoursStartUtc": "00:00",
  "WorkingHoursEndUtc": "00:00"
}
```

A background step compacts one closed week of sensor history or journal every 10 minutes (a week nothing was deleted from for 3 hours, counted from the server start at the earliest, and not compacted since its last delete; new values written into an old week, such as the row an aggregated sensor keeps rewriting, do not count). It is **off by default**. Sealed weeks get smaller on disk only through it, after their original rows are removed: with `HistorySealing.Enabled` on and this one off, the server logs a warning at start.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Turns the background compaction on. When off nothing is compacted in the background. |
| `WorkingHoursStartUtc`, `WorkingHoursEndUtc` | `00:00`, `00:00` | The daily UTC window it works in, `HH:mm`, as above. A compaction running when the window ends is finished, not stopped. |

On a large deployment enable it with a night window: the first start after that finds every closed week due. The **Compact** button under **Configuration → Database** does not depend on this section.

---

## Large Files

Collectors can send big files, crash dumps above all, as **large file values** of File sensors: the content travels in compressed pieces on a connection of its own and is kept on the server's disk, not in the database. Users download a file only as a zip, checked and recorded by HSM. How a collector sends them is on [DataCollector Large Files](DataCollector-Large-Files), what users see on [Large Files](Large-Files). Nothing needs to be switched on: a server takes large files with its defaults, and nothing is created on the disk before the first upload.

### The large files folder

The server keeps uploads in progress, checked files and finished archives in a folder of its own, `LargeFiles.Folder`: `LargeFiles` by default, relative to the working directory like `Databases` (so next to it; `/app/LargeFiles` in the container), or an absolute path on another volume.

- It is **not a database** and **not part of the backups** (sensor history is not either).
- Its layout is the server's own: `uploads/` (the parts of uploads, the records of all files, archives being built) and `archives/` (finished archives only). Do not rename, move or replace `archives/` while the server or the bundled Caddy runs: Caddy has that very directory mounted.
- **Size it for the files you expect.** A finished file takes the size of its archive (about the compressed size of the file: a dump often compresses three times or more); while it is uploaded and packed, its part and its archive are on the disk together.

**With Docker Compose** the folder is the host folder `HSM_LARGE_FILES` of `.env`, `./LargeFiles` next to `docker-compose.yml` by default. The reference compose file mounts it twice from that one variable: whole into the server at `/app/LargeFiles`, and its `archives/` subtree read-only into Caddy at `/srv/hsm-archives`. To put large files on another volume, set `HSM_LARGE_FILES` alone and leave `LargeFiles.Folder` at its default inside the container:

```dotenv
HSM_LARGE_FILES=/mnt/bigdisk/hsm-large-files
```

Then `docker compose up -d`. If the two mounts pointed at different folders, every download through Caddy would answer 404.

### Settings

```json
"LargeFiles": {
  "Folder": "LargeFiles",
  "MaxFileSizeBytes": 68719476736,
  "MaxUploadsInProgress": 4,
  "MinFreeDiskMb": 10240,
  "AbandonedUploadDays": 7,
  "LeaseTimeoutSeconds": 180,
  "MaxUploadingPartsPerProduct": 16
},
"LargeFileArchives": {
  "DeflateLevel": 1,
  "PasswordRetentionDays": 30,
  "DownloadLogWindowMinutes": 10,
  "DownloadLogRetentionDays": 365,
  "DownloadLogMaxCallers": 10000
}
```

`LargeFiles`:

| Setting | Default | Description |
|---|---|---|
| `Folder` | `LargeFiles` | The folder above. Must not be empty. |
| `MaxFileSizeBytes` | 68,719,476,736 (64 GiB) | The largest file the server accepts (1 to 2^50). A larger one is refused, and the collector reports it as not sent. |
| `MaxUploadsInProgress` | 4 | Uploads at once, over all collectors (1 to 1,024). One more waits: the server tells its collector to try again in 30 seconds. |
| `MinFreeDiskMb` | 10,240 (10 GiB) | The free space an upload must leave on the folder's volume (0 or more). See below. |
| `AbandonedUploadDays` | 7 | An upload that received no piece for this many days is removed (1 to 3,650). A checked file is never removed for its age. |
| `LeaseTimeoutSeconds` | 180 | How long an upload stays held for its collector without a new piece (10 to 86,400). See below. |
| `MaxUploadingPartsPerProduct` | 16 | Unfinished uploads one product may hold; a new one beyond them waits (1 to 100,000). |

`LargeFileArchives`:

| Setting | Default | Description |
|---|---|---|
| `DeflateLevel` | 1 | Compression of new archives, 0 (none) to 9. Level 1 builds about 1.7 times faster than 6 for an archive about 4% larger. |
| `PasswordRetentionDays` | 30 | How long a replaced dump password stays readable to the product's Managers and admins after the last archive under it was re-encrypted (0 to 3,650). |
| `DownloadLogWindowMinutes` | 10 | The requests of one user or token for one archive within this window make one record of the download log (1 to 1,440). |
| `DownloadLogRetentionDays` | 365 | Download records older than this are removed once a day (1 to 3,650). |
| `DownloadLogMaxCallers` | 10,000 | The windows of the download log kept in memory (1 to 10,000,000). |

Both sections are read when the server starts and written into `Config/appsettings.json` with their defaults. A value out of range stops the start with an error that names the key, for example `LargeFiles.MaxUploadsInProgress = 0 is invalid: expected 1..1024.`

`CollectorSessions.MaxFileFrameBytes` (4,194,304 by default; 131,072 to 67,108,864) is the largest piece frame a collector may send, and with it the piece size (4,173,824 bytes at the default). **0 switches file connections off**: sessions go on, large files wait on the collectors, and ordinary file values above about 1 MiB no longer fit a session frame.

**Free space.** The server begins a new upload only when the free space of the folder's volume, less `MinFreeDiskMb`, less what the uploads already begun have reserved, holds the new one's reservation: twice its size plus 1 MiB (the part and its archive). A reservation shrinks as the part arrives and ends when the archive is finished. A refused upload is not lost: the server asks the collector to wait 10 minutes (a collector asks again after at most 5) and the collector keeps the file. The refusals are counted in **HSM Server Monitoring → Large files → Refused for space**.

**The lease.** An upload belongs to one connection of its collector while pieces arrive. When no piece came for `LeaseTimeoutSeconds`, another connection may take it over (the collector continues on its next connection from the piece the server has). The lease is never shorter than the collector sessions' silence limit (three heartbeats, 90 seconds by default): a shorter setting is raised to it at start, with a warning in the log (`LargeFiles.LeaseTimeoutSeconds = 30 is shorter than the collector sessions' silence limit (90 s, three heartbeats): the lease is raised to 90 s, ...`).

### Downloads through the bundled Caddy

With the reference compose file a download is decided by HSM and served by Caddy:

1. The request reaches HSM through Caddy like any other. HSM checks the user (or API token), checks that the file may be given out, and writes the record of the download log.
2. Instead of the bytes, HSM answers Caddy with an instruction naming the archive. Caddy serves that file from its read-only mount of `archives/`, with byte ranges and resumption. The instruction never reaches the client.
3. The client sees `Server: Caddy`, the archive with `Content-Disposition`, `Repr-Digest` (its SHA-256) and, for a secure file, `X-Hsm-Password-Version`.

Caddy has no route of its own to the folder: an archive is reachable only as HSM's answer to a request it has checked. HSM honours Caddy's marker header (`X-Hsm-Serve-Archives`) only on a connection from a **trusted proxy**; the reference compose file trusts its own network for that (`Kestrel__TrustedProxies__0: 'attached-networks'`, see [Ports](#ports)).

A Caddyfile of your own ([Advanced Caddyfile customization](Installation#advanced-caddyfile-customization)) made from a bundled file older than the image `hsmonitoring/hsm-caddy:2.11.4-9` has no hand-over rule: HSM then serves the archives itself. Such a copy also passes a client's own `X-Hsm-Serve-Archives` header on to HSM, which then answers that client with an error instead of the file. Re-merge your copy with the current bundled file.

### The link in alert messages

The alerts of File sensors can print `$link`, the address of the sensor page (`https://<address>/Home/<sensor id>`; see [Large Files](Large-Files#the-alert)). The server takes the address from the environment variable `HSM_DOMAIN`, which the reference compose file passes to it from `.env`, or, without it, from `Agent.ExternalConnectionUrl`; without either, `$link` prints the path alone. `HSM_DOMAIN` has no port: on a server whose site is on a port other than 443 the links lack it, and whoever opens one adds it. `Agent.ExternalConnectionUrl` keeps its scheme and port.

### Without Caddy, or behind another reverse proxy

- **Without Caddy** (`docker-compose.direct.yml`, a plain `docker run`, a service install): HSM serves the archives itself, with byte ranges and resumption. `docker-compose.direct.yml` mounts the same `HSM_LARGE_FILES` folder.
- **Behind another reverse proxy** that you list in `Kestrel__TrustedProxies`: the proxy must **overwrite or remove** the request header `X-Hsm-Serve-Archives` on every request it forwards, whether or not it serves archives itself.
  - To let it serve the archives, repeat the bundled rule: set `X-Hsm-Serve-Archives: 1` on every forwarded request; on an answer that carries `X-Hsm-Archive: <path>`, serve the file at that path under a read-only view of the `archives/` folder instead of the answer, copy the headers `Content-Disposition`, `Repr-Digest`, `X-Hsm-Password-Version` and `Cache-Control`, and never pass `X-Hsm-Archive` on.
  - Otherwise remove the header, and HSM serves the files itself.
- A proxy that is **not** listed in `Kestrel__TrustedProxies` changes nothing: HSM ignores the marker from it and serves the files itself (and records the proxy's address as the client's, see [Ports](#ports)).

### What the server reports

The node **HSM Server Monitoring → Large files** appears once the server has taken its first upload:

| Sensor | What it shows |
|---|---|
| `Archive build result` | Error when an archive could not be built (the comment names the file and the reason) or waits for disk room; the file is kept and tried again. Has a default alert. |
| `Download log` | Error when a download record cannot be written: every download is refused meanwhile. Has a default alert. |
| `Refused for space` | uploads refused for free space since the start; Error in the sample in which it grew, with a default alert: `[$product]$path: a large file upload was refused, the disk is short ($value since the server started)` |
| `Archive builds failed` | archive builds (first packs and re-encryptions) that failed since the start; Error in the sample in which it grew |
| `Uploads in progress`, `Files stored`, `Bytes stored` (MB), `Files waiting for a password` | what the folder holds now, read from its records at every sample: uploads not finished, stored files of every product, the megabytes of the parts of uploads and of stored files and of the archives, secure files of products without a dump password |
| `Parts removed`, `Files removed`, `Leftovers removed` | what the server removed: abandoned or discarded parts, files whose value is gone (retention, a clear, a removed sensor), files left behind |
| `Start disagreements`, `Rows written again`, `Rows that disagree`, `Files of sensors not loaded`, `Write failures` | checks of the folder against the database; each is Error in the sample in which it grew, and the log names every case |

`Uploads in progress`, `Files stored`, `Bytes stored` and `Files waiting for a password` are read again at every sample (every 5 minutes); the other number sensors count since the server started. The server's log names every upload begun, stored or discarded, every archive built, and every removal (logger `LargeFiles`). The download log is a database of its own, `Databases/DownloadLog`.

---

## Docker Volume Mounts

Always mount these directories to preserve data across container restarts and updates:

```bash
docker run \
  -v /host/Logs:/app/Logs \
  -v /host/Config:/app/Config \
  -v /host/Databases:/app/Databases \
  -v /host/DatabasesBackups:/app/DatabasesBackups \
  -v /host/LargeFiles:/app/LargeFiles \
  -p 443:443 \
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

With Docker Compose, use the [reference docker-compose.yml](Installation#reference-docker-composeyml) instead of writing your own: it already mounts these directories.

| Directory | Contents |
|---|---|
| `Logs/` | Application logs |
| `Config/` | `appsettings.json` and TLS certificate |
| `Databases/` | LevelDB databases (sensor history, metadata, dashboards) |
| `DatabasesBackups/` | Automatic database backups |
| `LargeFiles/` | Large file values: uploads and finished archives ([Large Files](#large-files)); not part of the backups |

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
