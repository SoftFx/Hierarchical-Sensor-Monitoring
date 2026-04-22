# Quick Start

This page shows the minimal code to get HSMDataCollector running in a .NET application.

For full API reference see [HSM DataCollector](HSMDataCollector).

---

## 1. Install the package

```bash
dotnet add package HSMDataCollector
```

## 2. Start with default settings

```csharp
using HSMDataCollector.Core;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://your-hsm-server",
    AccessKey = "YOUR_ACCESS_KEY"  // from Products → Access Keys in the web UI
});

// Add built-in system sensors (optional)
collector.Windows.AddSystemMonitoringSensors()
                 .AddProcessMonitoringSensors()
                 .AddCollectorMonitoringSensors();

await collector.Start();

// Send a custom sensor value
var cpuSensor = collector.CreateDoubleSensor("MyApp/cpu_usage");
cpuSensor.AddValue(42.5);

// On shutdown:
await collector.Stop();
```

## 3. Add logging (optional)

```csharp
using HSMDataCollector.Logging;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://your-hsm-server",
    AccessKey = "YOUR_ACCESS_KEY",
    Module = "MyService"
})
.AddNLog(new LoggerOptions { WriteDebug = true });

collector.Windows.AddProcessMonitoringSensors()
                 .AddSystemMonitoringSensors()
                 .AddCollectorMonitoringSensors();

await collector.Start();
```

After `Start()`, open the web UI — your sensors will appear under the product associated with the access key.

---

## See Also

- [HSM DataCollector](HSMDataCollector) — Full DataCollector API reference
- [DataCollector Logging](DataCollector-logging) — Configure logging options
- [DataCollector Statuses](DataCollector-statuses) — Collector lifecycle states
- [Sensor Types](Sensor-types) — Available sensor types
- [Windows Sensors](Windows-sensors-collection) — Built-in Windows sensors
- [Unix Sensors](Unix-sensors-collection) — Built-in Unix/Linux sensors
