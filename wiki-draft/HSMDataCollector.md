# HSM DataCollector

HSMDataCollector is a .NET NuGet package for sending sensor data from your application to an HSM server. It handles batching, queuing, retries, and includes built-in sensors for system and process metrics.

---

## Installation

```bash
dotnet add package HSMDataCollector
```

Requires .NET 6 or later.

---

## Basic Usage

```csharp
using HSMDataCollector.Core;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://your-hsm-server",
    AccessKey = "YOUR_ACCESS_KEY"
});

await collector.Start();

// ... send data ...

await collector.Stop();
```

---

## CollectorOptions

All options with their defaults:

| Property | Default | Description |
|---|---|---|
| `ServerAddress` | `"localhost"` | HSM server address (include `https://`) |
| `Port` | `44330` | HSM server sensor port |
| `AccessKey` | — | **Required.** Access key from the product settings |
| `ClientName` | — | Identifier for this collector instance |
| `ComputerName` | — | Computer/host name shown in sensor paths |
| `Module` | — | Module name — shown as a node under the computer in the sensor tree |
| `MaxQueueSize` | `20000` | Maximum buffered values before old ones are dropped |
| `MaxValuesInPackage` | `1000` | Maximum values sent in a single HTTP request |
| `PackageCollectPeriod` | `15 sec` | How often buffered values are sent to the server |
| `ExceptionDeduplicatorWindow` | `1 hour` | Duplicate error messages within this window are suppressed |
| `DataSender` | — | Custom HTTP client implementation (optional) |

### Sensor Path Structure

Sensor paths depend on `ComputerName` and `Module`:

- Built-in **computer sensors** (CPU, RAM, disk): `ComputerName/sensor-name`
- Built-in **module sensors** (process metrics): `ComputerName/Module/sensor-name`
- **Custom sensors**: path is exactly what you pass to `Create*Sensor()`

---

## Lifecycle

```csharp
var collector = new DataCollector(options);

// Optional: attach logging before Start
collector.AddNLog(new LoggerOptions { WriteDebug = true });

await collector.Start();   // connects to server, starts background tasks

// ... application runs ...

await collector.Stop();    // flushes queue, stops background tasks
```

**Status events** (subscribe before `Start`):
```csharp
collector.ToStarting += () => Console.WriteLine("Starting...");
collector.ToRunning  += () => Console.WriteLine("Running");
collector.ToStopping += () => Console.WriteLine("Stopping...");
collector.ToStopped  += () => Console.WriteLine("Stopped");
```

**Test connection without starting:**
```csharp
var result = await collector.TestConnection();
if (!result.IsOk)
    Console.WriteLine(result.Error);
```

---

## Built-in Sensors

### Windows

Add via `collector.Windows.*`:

**System (computer-level):**
```csharp
collector.Windows.AddSystemMonitoringSensors();
// Adds: Total CPU, Free RAM, .NET GC time
// Path: ComputerName/...
```

**Process (module-level):**
```csharp
collector.Windows.AddProcessMonitoringSensors();
// Adds: Process CPU, Memory, Thread Count, GC time
// Path: ComputerName/Module/...
```

**Disk:**
```csharp
collector.Windows.AddFreeDiskSpace();           // Free space per disk
collector.Windows.AddFreeDiskSpacePrediction(); // Trend-based forecast
collector.Windows.AddActiveDiskTime();          // I/O activity %
collector.Windows.AddDiskQueueLength();         // I/O queue depth
collector.Windows.AddDiskAverageWriteSpeed();   // Write throughput
```

**Windows-specific:**
```csharp
collector.Windows.AddWindowsLastUpdate();       // Last Windows Update date
collector.Windows.AddWindowsLastRestart();      // Last reboot time
collector.Windows.AddWindowsVersion();          // OS version string
collector.Windows.AddWindowsApplicationErrorLogs();   // Event Log error count
collector.Windows.AddWindowsApplicationWarningLogs(); // Event Log warning count
```

**Convenience:**
```csharp
collector.Windows.AddAllDefaultSensors();  // All of the above
collector.Windows.AddAllComputerSensors(); // All computer-level sensors
collector.Windows.AddAllModuleSensors();   // All module-level sensors
```

### Unix/Linux

Add via `collector.Unix.*` — same pattern, subset of Windows sensors:

```csharp
collector.Unix.AddSystemMonitoringSensors();
collector.Unix.AddProcessMonitoringSensors();
collector.Unix.AddFreeDiskSpace();
collector.Unix.AddFreeDiskSpacePrediction();
```

---

## Custom Sensors

### Instant Sensors

Send a value immediately when `AddValue()` is called.

```csharp
// Create once, reuse many times
var boolSensor    = collector.CreateBoolSensor("MyApp/is_running");
var intSensor     = collector.CreateIntSensor("MyApp/queue_depth");
var doubleSensor  = collector.CreateDoubleSensor("MyApp/cpu_usage");
var stringSensor  = collector.CreateStringSensor("MyApp/last_error");
var timeSensor    = collector.CreateTimeSensor("MyApp/request_duration");
var versionSensor = collector.CreateVersionSensor("MyApp/version");

// Send values
boolSensor.AddValue(true);
intSensor.AddValue(42);
doubleSensor.AddValue(87.3);
stringSensor.AddValue("connection timeout");
timeSensor.AddValue(TimeSpan.FromMilliseconds(350));
versionSensor.AddValue(new Version(2, 1, 0));
```

All `AddValue()` calls accept an optional `comment` parameter:
```csharp
doubleSensor.AddValue(87.3, comment: "after GC");
```

### Last Value Sensors

Track the most recently set value and send it periodically. Useful for state that changes infrequently.

```csharp
var statusSensor = collector.CreateLastValueBoolSensor("MyApp/connected");

statusSensor.AddValue(true);   // stored, sent on next flush
// If no new value arrives, the last one is resent
```

### Bar Sensors (Aggregated)

Collect many values over a time window and send statistics (min, max, mean, count, first, last).

```csharp
// Default 5-minute window
var intBar    = collector.CreateIntBarSensor("MyApp/response_time_ms");
var doubleBar = collector.CreateDoubleBarSensor("MyApp/cpu_samples");

// Predefined window sizes
var bar1m  = collector.Create1MinDoubleBarSensor("MyApp/fast_metric");
var bar5m  = collector.Create5MinIntBarSensor("MyApp/requests");
var bar10m = collector.Create10MinDoubleBarSensor("MyApp/throughput");
var bar30m = collector.Create30MinIntBarSensor("MyApp/errors");
var bar1h  = collector.Create1HourDoubleBarSensor("MyApp/hourly");

// Add individual measurements
intBar.AddValue(123);
intBar.AddValue(456);
// Sent as: min=123, max=456, mean=289.5, count=2 after window closes
```

### Rate Sensors

Send a value formatted as a rate (units per second).

```csharp
var rate   = collector.CreateRateSensor("MyApp/requests_per_sec");
var m1Rate = collector.CreateM1RateSensor("MyApp/1min_rate");
var m5Rate = collector.CreateM5RateSensor("MyApp/5min_rate");

rate.AddValue(150.0);
```

### Function Sensors

Automatically invoke a function on a timer and send the result.

```csharp
// No-parameter function, called every minute
var funcSensor = collector.Create1MinFuncDoubleSensor(
    "MyApp/memory_mb",
    () => GC.GetTotalMemory(false) / 1024.0 / 1024.0
);

// Function receiving a list of accumulated values
var valueFuncSensor = collector.CreateValuesDoubleFuncSensor(
    "MyApp/avg_latency",
    values => values.Average()
);
valueFuncSensor.AddValue(120.0);
valueFuncSensor.AddValue(80.0);
// Sends 100.0 (average) on next tick
```

### File Sensors

```csharp
var fileSensor = collector.CreateFileSensor("MyApp/report");
await fileSensor.SendFileAsync("/path/to/report.csv");
```

### Enum Sensors

```csharp
var enumSensor = collector.CreateEnumSensor("MyApp/state");
enumSensor.AddValue(2); // integer value of the enum
```

---

## Sensor Options

All `Create*Sensor()` methods accept an optional `SensorOptions` parameter:

```csharp
var sensor = collector.CreateDoubleSensor("MyApp/cpu", new InstantSensorOptions
{
    Description = "CPU usage of the application process",
    TTL = TimeSpan.FromMinutes(5),       // alert if silent for 5 min
    KeepHistory = TimeSpan.FromDays(30), // retain 30 days of history
    SelfDestroy = TimeSpan.FromDays(7),  // auto-delete if no data for 7 days
});
```

**Bar sensor options:**
```csharp
var bar = collector.CreateDoubleBarSensor("MyApp/metric", new BarSensorOptions
{
    BarPeriod = TimeSpan.FromMinutes(10),  // aggregation window (default: 5 min)
    BarTickPeriod = TimeSpan.FromSeconds(5), // internal tick interval
    Precision = 2,                         // decimal places for mean
});
```

---

## Logging

```csharp
using HSMDataCollector.Logging;

collector.AddNLog(new LoggerOptions
{
    WriteDebug = true  // enable verbose debug output
});

// Or provide a custom logger:
collector.AddCustomLogger(new MyCustomLogger());
```

---

## Collector Status

```csharp
Console.WriteLine(collector.Status);
// Starting | Running | Stopping | Stopped
```

```csharp
Console.WriteLine(collector.ComputerName);
Console.WriteLine(collector.Module);

foreach (var sensor in collector.DefaultSensors)
    Console.WriteLine(sensor.Path);
```
