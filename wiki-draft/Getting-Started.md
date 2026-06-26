# Getting Started

This page walks you through installing HSM and sending your first sensor value in about 5 minutes.

---

## Step 1 — Install HSM Server

The easiest way to run HSM is with Docker Compose.

**1. Download the compose file:**
```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
```

**2. Start HSM:**
```bash
docker compose up -d
```

**3. Open the web UI:**

Navigate to `https://localhost:44333` in your browser.
Accept the self-signed certificate warning if prompted.

**Default credentials:**
- Login: `default`
- Password: `default`

> Change the default password immediately after first login.

**Ports:**
| Port | Purpose |
|---|---|
| `44333` | Web UI (HTTPS) |
| `44330` | Sensor data ingestion (HTTPS) |

For other installation methods, see [Installation](Installation).

---

## Step 2 — Create a Product and Access Key

Before sending data, you need a **Product** (a logical container for your sensors) and an **Access Key** to authenticate.

1. Go to **Products** → click **+ Add product** → enter a name → confirm
2. Open the product → go to **Access Keys** → click **+ Add key**
3. Copy the generated key — you will use it in the next step

---

## Step 3 — Send Your First Sensor Value

### Option A: HSMDataCollector (for .NET applications)

Install the NuGet package:
```bash
dotnet add package HSMDataCollector
```

Minimal setup:
```csharp
using HSMDataCollector.Core;

var options = new CollectorOptions
{
    ServerAddress = "https://localhost",
    AccessKey = "YOUR_ACCESS_KEY"
};

var collector = new DataCollector(options);

// Optional: add built-in system sensors
collector.Windows.AddProcessMonitoringSensors()
                 .AddSystemMonitoringSensors();

await collector.Start();

// Send a custom value
var cpuSensor = collector.CreateDoubleSensor("MyApp/cpu_usage");
cpuSensor.AddValue(42.5);

// On application shutdown:
await collector.Stop();
```

For full API reference, see [HSM DataCollector](HSMDataCollector).

### Option B: REST API (any language)

Send a double value:
```bash
curl -X POST https://localhost:44330/api/sensors/double \
  -H "Content-Type: application/json" \
  -H "Key: YOUR_ACCESS_KEY" \
  -d '{
    "path": "MyApp/cpu_usage",
    "value": 42.5,
    "comment": "test value"
  }'
```

For all available endpoints, see [REST API](REST-API).

---

## Step 4 — View the Sensor in the Web UI

1. Open `https://localhost:44333`
2. Go to **Products** → open your product
3. You should see `MyApp/cpu_usage` in the sensor tree with the value you sent

---

## Step 5 — Create Your First Alert

1. Click on the sensor → open **Alerts**
2. Click **+ Add alert**
3. Set a condition, e.g.:
   - Property: `Value`
   - Operation: `>`
   - Target: `90`
4. Set the message: `[$product] $sensor is high: $value%`
5. Set destination: **From parent** (uses the product's default Telegram chats)
6. Save

To receive the alert in Telegram, connect a chat first — see [Telegram Setup](Telegram-Setup).

---

## What's Next

- [HSM DataCollector](HSMDataCollector) — full NuGet API reference
- [Alerts Overview](Alerts-Overview) — all alert features
- [Telegram Setup](Telegram-Setup) — connect notification chats
- [Server Configuration](Server-Configuration) — ports, TLS, backups
