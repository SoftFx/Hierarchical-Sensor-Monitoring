# HSM Server Monitoring

HSM Server monitors itself using the same HSMDataCollector library it provides to users. All self-monitoring data is collected under a dedicated product called **"HSM Server Monitoring"** which is created automatically on first launch.

> Do not use this product to store your own application data — it is reserved for server internals.

---

## Sensor Tree Structure

```
HSM Server Monitoring
├── computer/               ← Standard system sensors (CPU, RAM, Disk, etc.)
│   └── module/             ← Standard process sensors
├── Backup/                 ← Database backup result, sizes and duration
├── Database/               ← Database size and statistics
├── Clients/                ← Per-client API traffic and sensor update rates
├── Telegram Bot/           ← Telegram notification statistics
└── ...                     ← Other internal nodes
```

---

## System & Process Sensors (computer / module)

Standard sensors collected by HSMDataCollector, same as any user application would get from `AddAllDefaultSensors()`.

**On Windows:**
- Total CPU usage
- Free RAM (MB)
- .NET GC time
- Process CPU, Memory, Thread Count
- Free disk space per drive + prediction
- Windows last update / restart / version
- Application error and warning log counts

**On Linux:**
- Total CPU, Free RAM
- Process CPU, Memory, Thread Count
- Free disk space + prediction

---

## Database Node

Database size sensors are updated **once per day**.

| Sensor | Description |
|---|---|
| `History data size` | Size of sensor history database (weekly folders) |
| `Journals data size` | Size of journal/audit log database |
| `Config data size` | Sum of EnvironmentData + ServerLayout + Snapshots databases |
| `Environment data size` | Size of the EnvironmentData database (backed up) |
| `Dashboards data size` | Size of the ServerLayout (dashboards) database (backed up) |
| `Total data size` | Total size of all databases combined |

All sizes are reported in **MB**. A flat or shrinking `History data size` is a signal in itself.

**What each database contains:**
- **EnvironmentData** — metadata: folders, products, sensors, users, access keys
- **ServerLayout** — dashboard configuration: charts, panels, layouts
- **Snapshots** — current state of the sensor tree: last update times, TTL states
- **History** — historical sensor values (divided into weekly folders)
- **Journals** — audit log records per sensor

---

## Backup Node

Fed by the server's database backup (Configuration page → backup settings). Each run writes one zip per database into `DatabasesBackups/`: `EnvironmentData_<time>.zip` and `ServerLayout_<time>.zip`.

> Only the **EnvironmentData** (metadata) and **ServerLayout** (dashboards) databases are backed up. The sensor values (History), Journals and Snapshots databases are **not**.

| Sensor | Type / unit | Posted | Description |
|---|---|---|---|
| `Backup result` | bool | once per run | `true` + **Ok** when both databases were written (comment lists the files); `false` + **Error** when a backup failed (comment = the error). TTL = backup period × 1.5, so a run that never happens turns the sensor to **Timeout** |
| `Backup duration` | seconds | once per run | Time to write and zip both backups (pruning included, SFTP upload excluded) |
| `Environment backup size` | MB | once per run, on success | Size of the EnvironmentData zip written by the run |
| `Dashboards backup size` | MB | once per run, on success | Size of the ServerLayout zip written by the run |
| `Local backup size` | MB | once per run | Size of the whole `DatabasesBackups/` folder (all retained backups); Error status when a backup failed |
| `Remote backup size` | MB | once per run, SFTP enabled | Total size on the SFTP target; Error status when the upload failed |

The backup period (default 1 hour) is read at server start: after changing it, restart the server for both the schedule and the `Backup result` TTL to follow.

---

## Clients Node

API traffic statistics per connected client (DataCollector instance or REST API caller). Updated in real time as requests arrive.

Each client gets its own sub-node under `Clients/`:

| Sensor | Unit | Description |
|---|---|---|
| `Clients requests count` | req/sec | Total number of public API requests from this client |
| `Sensors updates` | sensors/sec | Number of individual sensor values received |
| `Traffic In` | KB/sec | Incoming data volume from this client |
| `Traffic Out` | KB/sec | Outgoing response volume to this client |

There is also a **Total** node that aggregates all clients combined.

---

## Telegram Bot Node

Monitoring of the HSM Telegram bot's notification activity.

| Sensor | Description |
|---|---|
| `Total` | Rate of notifications sent (per minute) |
| `Errors` | Telegram bot errors (failed sends, API errors) |
| `Messages/<chat-name>` | Last sent message per connected Telegram chat |

---

## Accessing the Data

Open the **HSM Server Monitoring** product in the web UI to see all sensors. You can set up alert policies on any of these sensors — for example:

- Alert if `Total data size` exceeds 10 GB
- Alert on `Backup result` status Error or Timeout (backup failed or stopped running)
- TTL alert if `History data size` stops updating (server issue)
- Alert if `Errors` in Telegram Bot node is non-empty
