# HSM DataCollector

HSMDataCollector is a .NET NuGet package for sending sensor data from your application to an HSM server. It handles connection management, value buffering, batching, and includes built-in sensors for system and process metrics.

---

## Installation

```bash
dotnet add package HSMDataCollector
```

Requires .NET 6 or later.

---

## Minimal Setup

```csharp
using HSMDataCollector.Core;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://your-hsm-server",
    AccessKey = "YOUR_ACCESS_KEY"
});

await collector.Start();

// your application runs here

await collector.Stop();
```

---

## CollectorOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `ServerAddress` | string | `"localhost"` | HSM server address — include `https://` |
| `Port` | int | `443` | HSM server port (443 since #1590). `44330` of the old two-port scheme is no longer served (#1696): a collector still configured with it must be set to 443 |
| `AccessKey` | string | — | **Required.** Access key from the product settings |
| `ClientName` | string | — | Identifier for this collector instance (shown in server logs) |
| `ComputerName` | string | — | Host name used in built-in sensor paths. Defaults to `Environment.MachineName` if not set |
| `Module` | string | — | Module name — appears as a sub-node under `ComputerName` in the sensor tree |
| `MaxQueueSize` | int | `20000` | Max buffered values. When exceeded, **oldest values are silently dropped** |
| `MaxValuesInPackage` | int | `1000` | Max values sent in a single HTTP request |
| `PackageCollectPeriod` | TimeSpan | `15 sec` | How often buffered values are flushed to the server |
| `ExceptionDeduplicatorWindow` | TimeSpan | `1 hour` | Identical error messages within this window are deduplicated — only the first is sent |
| `DataSender` | IDataSender | — | Custom HTTP client implementation. Leave `null` to use the default HTTPS client |
| `DataFolder` | string | `null` | (4.2.0) Where the collector keeps what survives a restart, today the state of large files. `null` takes `%LOCALAPPDATA%\HSMDataCollector` (Windows) or `~/.local/share/HSMDataCollector`; each collector gets a subfolder of its own |

### How ComputerName and Module Affect Paths

Built-in sensors are placed in the sensor tree based on these two options:

```
ComputerName/                     ← computer-level sensors (CPU, RAM, disk, Windows info)
ComputerName/Module/              ← module-level sensors (process CPU, memory, threads)
```

Custom sensors created with `CreateXxxSensor("some/path")` are placed at the exact path you provide, unaffected by `ComputerName` or `Module`.

---

## Lifecycle

```csharp
var collector = new DataCollector(options);

// Attach logging before Start (optional)
collector.AddNLog(new LoggerOptions { WriteDebug = true });

// Subscribe to status events before Start (optional)
collector.ToStarting += () => Console.WriteLine("Starting...");
collector.ToRunning  += () => Console.WriteLine("Running");
collector.ToStopping += () => Console.WriteLine("Stopping...");
collector.ToStopped  += () => Console.WriteLine("Stopped");

await collector.Start();   // connects to server, starts background flush loop

// application runs here

await collector.Stop();    // flushes remaining queue, stops all background tasks
```

**Test connection before starting:**
```csharp
var result = await collector.TestConnection();
if (!result.IsOk)
    Console.WriteLine($"Cannot connect: {result.Error}");
```

---

## Built-in Sensors

### Windows

Call these before `Start()`. All methods return the collection so they can be chained.

```csharp
collector.Windows
    .AddSystemMonitoringSensors()     // Total CPU, Free RAM, System GC time
    .AddProcessMonitoringSensors()    // Process CPU, Memory, Threads, Process GC time
    .AddCollectorMonitoringSensors()  // DataCollector diagnostics (see below)
    .AddFreeDiskSpace()               // Free disk space per drive (MB)
    .AddFreeDiskSpacePrediction()     // Days until disk is full (trend-based)
    .AddActiveDiskTime()              // Disk I/O activity %
    .AddDiskQueueLength()             // I/O queue length
    .AddDiskAverageWriteSpeed()       // Disk write throughput
    .AddWindowsLastUpdate()           // Date of last Windows Update (sent every 12h)
    .AddWindowsLastRestart()          // Last system restart time (sent every 12h)
    .AddWindowsVersion()              // OS version string (sent every 12h)
    .AddWindowsApplicationErrorLogs()   // Windows Event Log error count
    .AddWindowsApplicationWarningLogs() // Windows Event Log warning count
    ;

// Convenience: add all of the above at once
collector.Windows.AddAllDefaultSensors();

// Or split by level:
collector.Windows.AddAllComputerSensors(); // all computer-level sensors
collector.Windows.AddAllModuleSensors();   // all module-level sensors
```

**Windows Service Status** (Windows only):
```csharp
collector.Windows.AddWindowsServiceStatus(new ServiceSensorOptions("MyServiceName"));
// Monitors the named Windows service status
// Sends an Enum value: Running=4, Stopped=1, Paused=7, etc.
// Sends value only when the status changes
// Sends Error status if the service is not found (retries every 1 hour)
```

### Unix / Linux

```csharp
collector.Unix
    .AddSystemMonitoringSensors()     // Total CPU, Free RAM
    .AddProcessMonitoringSensors()    // Process CPU, Memory, Thread Count
    .AddCollectorMonitoringSensors()  // DataCollector diagnostics
    .AddFreeDiskSpace()               // Free disk space
    .AddFreeDiskSpacePrediction()     // Days until disk full
    ;

collector.Unix.AddAllDefaultSensors();
```

### Collector Monitoring Sensors (AddCollectorMonitoringSensors)

These sensors expose the DataCollector's own internal health. Always recommended.

| Sensor | Type | Description |
|---|---|---|
| `Collector alive` | Bool | Sends `true` periodically — use for TTL alert to detect collector crash |
| `Collector errors` | String | Internal DataCollector errors (connection failures, exceptions) |
| `Queue overflow` | IntBar | Count of values dropped due to queue overflow (should be 0) |
| `Package content size` | IntBar | Number of values per sent package |
| `Package process time` | DoubleBar | Per sent package, the average time (seconds) its values waited in the send queue before the package was collected |
| `Package data count` | IntBar | Total values sent per package |
| `Product version` | Version | Version of the monitored application |

---

## Sensor Options

All `Create*Sensor()` methods accept an optional options object. The base fields available on all sensor types:

| Option | Type | Default | Description |
|---|---|---|---|
| `Description` | string | — | Human-readable description shown in the web UI |
| `TTL` | TimeSpan? | — | Inactivity timeout — sensor goes OffTime if no value arrives within this window |
| `KeepHistory` | TimeSpan? | — | How long to keep historical values. Default is server-wide setting |
| `SelfDestroy` | TimeSpan? | — | Auto-delete the sensor if no data for this duration |
| `SensorUnit` | Unit? | — | Unit of measurement shown in the UI (e.g. `Unit.Percent`, `Unit.MB`, `Unit.KBytes_sec`) |
| `EnableForGrafana` | bool? | — | Whether this sensor appears in Grafana datasource |
| `IsSingletonSensor` | bool? | — | If true, only one instance of this sensor path exists across all collectors |
| `AggregateData` | bool? | — | Whether to aggregate values before sending |
| `IsPrioritySensor` | bool? | — | If true, values are sent in a separate priority request |
| `IsForceUpdate` | bool? | — | If true, DataCollector can overwrite user-configured settings on the server |
| `TtlAlert` | SpecialAlertTemplate | — | Configure a custom TTL alert template |
| `Alerts` | List | — | Pre-configured alert rules applied when sensor is created |

### InstantSensorOptions example

```csharp
var sensor = collector.CreateDoubleSensor("MyApp/cpu", new InstantSensorOptions
{
    Description = "CPU usage of the main process",
    TTL = TimeSpan.FromMinutes(5),
    KeepHistory = TimeSpan.FromDays(30),
    SelfDestroy = TimeSpan.FromDays(14),
    SensorUnit = Unit.Percent,
    EnableForGrafana = true,
});
```

### BarSensorOptions example

```csharp
var bar = collector.CreateDoubleBarSensor("MyApp/response_ms", new BarSensorOptions
{
    BarPeriod = TimeSpan.FromMinutes(10), // aggregation window (default: 5 min)
    BarTickPeriod = TimeSpan.FromSeconds(5), // how often intermediate state is posted
    Precision = 2,                        // decimal places for mean/min/max
    Description = "HTTP response times in ms",
});
```

### DiskSensorOptions example

```csharp
collector.Windows.AddFreeDiskSpace(new DiskSensorOptions
{
    TargetPath = @"D:\",  // which drive to monitor (default: C:\)
});
```

---

## Custom Sensors

### Instant sensors — send value immediately

```csharp
var boolSensor    = collector.CreateBoolSensor("MyApp/is_healthy");
var intSensor     = collector.CreateIntSensor("MyApp/queue_depth");
var doubleSensor  = collector.CreateDoubleSensor("MyApp/cpu_pct");
var stringSensor  = collector.CreateStringSensor("MyApp/last_error");
var timeSensor    = collector.CreateTimeSensor("MyApp/request_duration");
var versionSensor = collector.CreateVersionSensor("MyApp/version");
var rateSensor    = collector.CreateRateSensor("MyApp/rps");

boolSensor.AddValue(true);
intSensor.AddValue(42);
doubleSensor.AddValue(87.3);
stringSensor.AddValue("timeout on db connection");
timeSensor.AddValue(TimeSpan.FromMilliseconds(350));
versionSensor.AddValue(new Version(2, 1, 0));
rateSensor.AddValue(150.0);
```

All `AddValue()` calls accept an optional `comment`:
```csharp
doubleSensor.AddValue(87.3, comment: "after GC pause");
```

### Last value sensors — resend last value periodically

```csharp
var sensor = collector.CreateLastValueBoolSensor("MyApp/connected");
sensor.AddValue(true);
// If no new value is added before the flush, the last value is resent automatically
```

### Bar sensors — aggregate over time window

```csharp
// Default 5-minute window
var intBar    = collector.CreateIntBarSensor("MyApp/response_ms");
var doubleBar = collector.CreateDoubleBarSensor("MyApp/cpu_samples");

// Predefined windows
var bar1m  = collector.Create1MinDoubleBarSensor("MyApp/fast_metric");
var bar5m  = collector.Create5MinIntBarSensor("MyApp/requests");
var bar10m = collector.Create10MinDoubleBarSensor("MyApp/throughput");
var bar30m = collector.Create30MinIntBarSensor("MyApp/slow_metric");
var bar1h  = collector.Create1HourDoubleBarSensor("MyApp/hourly");

// Feed individual measurements
intBar.AddValue(120);
intBar.AddValue(340);
// After window closes → sent as: min=120, max=340, mean=230, count=2
```

### Rate sensors

```csharp
var rate   = collector.CreateRateSensor("MyApp/requests_per_sec");
var m1Rate = collector.CreateM1RateSensor("MyApp/1min_rate");
var m5Rate = collector.CreateM5RateSensor("MyApp/5min_rate");

rate.AddValue(1);  // call once per event — rate is computed automatically
```

### Function sensors — run on a timer

```csharp
// Call a no-arg function every minute, send result
var memSensor = collector.Create1MinFuncDoubleSensor(
    "MyApp/heap_mb",
    () => GC.GetTotalMemory(false) / 1_048_576.0
);

// Accumulate values, call aggregator every minute
var avgSensor = collector.CreateValuesDoubleFuncSensor(
    "MyApp/avg_latency_ms",
    values => values.Count > 0 ? values.Average() : 0
);
// Feed individual request durations:
avgSensor.AddValue(120.0);
avgSensor.AddValue(80.0);
// Every minute → sends 100.0 (average), resets the list
```

### Enum sensors

```csharp
var stateSensor = collector.CreateEnumSensor("MyApp/state");
stateSensor.AddValue(2); // send integer value of the enum
```

### File sensors

```csharp
var fileSensor = collector.CreateFileSensor("MyApp/daily_report");
await fileSensor.SendFileAsync("/path/to/report.csv");
```

A file value is limited to 10 MiB by default (`FileSensorOptions.MaxFileSizeBytes`); larger files, such as dumps, are
sent as **large files** (below). On the session transport (4.2.0), when the server offers large files, a file value larger than about 1 MiB (a quarter
of the server's session frame) travels on the large-file connection and is stored as a large file value without a password; it
has no preview on the sensor page.

### Large files (4.2.0)

A large file (a dump of up to tens of gigabytes) is sent as a value of a File sensor whose content is kept on the
server's disk: the collector reads it in pieces, compresses each piece with zstd, sends them one at a time on the
collector session and continues after a cut from where the server stopped. Large files need the session transport (the
default); the server must support them.

```csharp
using HSMDataCollector.Core;
using HSMDataCollector.Options;

// Every *.dmp file that appears in the folder is sent once it has not changed for a minute,
// and deleted after the server confirmed it stored the whole file.
var watch = collector.WatchLargeFiles("MyApp/dumps", @"C:\MyApp\dumps", "*.dmp");

// Or one file the application knows is complete:
LargeFileResult result = await collector.SendLargeFileAsync("MyApp/dumps", @"C:\MyApp\dumps\now.dmp",
    new LargeFileOptions { DeleteAfterUpload = false });
```

| `LargeFileOptions` | Default | Meaning |
|---|---|---|
| `SettleTime` | 60 s | a watched file is taken when its size and time have not changed for this long (and, on Windows, nobody writes it) |
| `DeleteAfterUpload` | `true` | delete the source after the server stored it; `false` keeps it and does not send it again |
| `MaxBytesPerSecond` | 10,000,000 | the most bytes per second the upload sends; `0` means no limit |
| `NewFileAlert` | `true` | the sensor gets the server's default alert "new data has been received" |
| `Secure` | `true` | the server gives the file out only as a zip under the product's dump password; `false`: a zip without a password, open to everyone who sees the product |
| `SensorOptions` | `null` | the File sensor's registration (description, location, history period...) |

One file is sent at a time, on a background thread of lowered priority; the calls may be made from any thread and in any
state of the collector, and Stop does not wait for an upload: it continues after the next Start. Files that cannot be
sent (refused by the server, over its limit, failing again and again) are left where they are, written to the
collector's errors and counted in the `Queue overflow` sensor under `Large files`. The state (which files were delivered,
the upload in progress) is kept in `CollectorOptions.DataFolder`.

---

## Logging

```csharp
using HSMDataCollector.Logging;

// NLog integration
collector.AddNLog(new LoggerOptions
{
    WriteDebug = true  // include verbose debug output
});

// Custom logger
collector.AddCustomLogger(new MyLogger()); // implement ICollectorLogger
```

---

## Diagnostics

```csharp
// Current collector state
Console.WriteLine(collector.Status);
// Starting | Running | Stopping | Stopped

// Configured identifiers
Console.WriteLine(collector.ComputerName);
Console.WriteLine(collector.Module);

// All built-in sensors
foreach (var sensor in collector.DefaultSensors)
    Console.WriteLine(sensor.Path);
```
