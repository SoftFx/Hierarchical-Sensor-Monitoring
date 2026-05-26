# Getting Started

HSM is a self-hosted monitoring platform. You deploy one server, connect your applications to it, and get real-time alerts when something goes wrong.

---

## How It Works

```
Your Application
  └── HSMDataCollector (NuGet)  ──►  HSM Server  ──►  Telegram
       or REST API                      │
                                        └──► Web UI (graphs, history, alerts)
```

1. **Your application** sends sensor values — metrics, statuses, any numeric or text data
2. **HSM Server** receives them, stores history, and evaluates alert rules on each value
3. When a rule triggers — **Telegram notification** is sent to the configured chats
4. You can also view all data and manage settings in the **Web UI**

---

## Key Concepts

**Product** — a container for one application or service. Holds all its sensors and access keys.

**Sensor** — a single data stream identified by a path (e.g. `MyApp/database/query_time_ms`). Created automatically the first time data is sent to a new path.

**Access Key** — authenticates data coming from your application. Created per product.

**Folder** — groups products together. Telegram chats are connected at the folder level.

**Alert Policy** — a rule on a sensor: if value > 90 → send message to Telegram chat.

**TTL** — if a sensor stops sending data for too long → OffTime alert.

---

## Where to Go Next

### I want to deploy the server
→ [Installation](Installation) — Docker Compose, docker run, scripts

### I want to configure the server
→ [Server Configuration](Server-Configuration) — ports, TLS, backups, Telegram bot setup

### I want to send data from my .NET application
→ [HSM DataCollector](HSMDataCollector) — NuGet package, CollectorOptions, built-in and custom sensors

### I want to send data from any other language
→ [REST API](REST-API) — HTTP endpoints with JSON examples for all sensor types

### I want to set up alerts
→ [Alerts Overview](Alerts-Overview) — conditions, templates, TTL, scheduling, inheritance

### I want to connect Telegram notifications
→ [Telegram Setup](Telegram-Setup) — step-by-step for direct and group chats

### I don't know what sensor type to use
→ [Alert Conditions Reference](Alert-Conditions-Reference) — all types and their available conditions

### I want to apply the same alert to many sensors
→ [Alert Templates](Alert-Templates) — path pattern matching, auto-apply

### I want to suppress alerts outside working hours
→ [Alert Schedules](Alert-Schedules) — YAML-based working hours configuration
