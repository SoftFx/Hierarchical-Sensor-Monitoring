# Hierarchical Sensor Monitoring (HSM)

HSM is a self-hosted monitoring platform for collecting sensor data from your services and applications, with a powerful and flexible alert system.

**The core value of HSM is its alert engine.** You define rules — when CPU exceeds 90%, when a service stops sending data, when a status changes — and HSM notifies you via Telegram, with full control over schedules, templates, and thresholds.

---

## What HSM Does

- Collects metrics and status values from your applications via a [NuGet client library](HSMDataCollector) or [REST API](REST-API)
- Organizes sensors in a hierarchical tree: Product → Folder → Sensor
- Evaluates alert policies on every incoming value in real time
- Sends notifications to Telegram chats when conditions are met
- Displays sensor history and current status in a web UI
- Supports Grafana as an external dashboard

---

## Architecture

```
Your Application
  └── HSMDataCollector (NuGet)
        └── HTTPS ──► HSM Server (port 44330)
                          ├── Alert Engine
                          ├── LevelDB Storage
                          └── Web UI (port 44333)
                                └── Telegram Bot → Your Chats
```

**HSM Server** — ASP.NET Core service. Receives sensor data, evaluates policies, stores history, serves the web UI, and operates the Telegram bot.

**HSMDataCollector** — NuGet package for .NET applications. Collects system metrics (CPU, memory, disk) and sends custom sensor values to the server.

**REST API** — HTTP endpoints for any language or script that cannot use the NuGet package.

---

## Quick Navigation

| I want to... | Go to |
|---|---|
| Install HSM | [Installation](Installation) |
| Send my first sensor value | [Quick Start](Quick-Start) |
| Set up alert notifications | [Alerts Overview](Alerts-Overview) |
| Connect Telegram | [Telegram Setup](Telegram-Setup) |
| Configure alert schedules | [Alert Schedules](Alert-Schedules) |
| Apply alerts to many sensors at once | [Alert Templates](Alert-Templates) |
| See all available conditions | [Alert Conditions Reference](Alert-Conditions-Reference) |
| Use the NuGet library | [HSM DataCollector](HSMDataCollector) |
| Send data via HTTP | [REST API](REST-API) |
| Configure the server | [Server Configuration](Server-Configuration) |

---

## Sensor Status at a Glance

| Icon | Status | Meaning |
|---|---|---|
| 🟢 | Ok | No active alerts |
| 🔴 | Error | At least one alert policy is triggered |
| 🕑 | OffTime | Sensor has not sent data within the TTL window |
