# DataCollector Table Sensors

> **Draft, not released yet.** This page describes the table sensors branch (`feature/1842-table-sensors`, epic #1842): the managed collector HSMDataCollector 4.1.0 (`4.1.0-preview` in the branch), the native collector 0.15.1, the C++ wrapper 2.0.0, and a server later than 3.44.0. The released server 3.44.0 has no table sensors.

This page is for a developer who sends rows from an application. What a table sensor is, what the site shows and how to read rows back is on [Table Sensors](Table-Sensors). All kinds of sensors are compared on [Sensor Types](Sensor-types).

In short: you declare a table sensor with its columns, add rows from any thread, and the collector sends the rows of all its table sensors together every 10 seconds over its **collector session**. Nothing ever throws because the network or the server is not there: rows that cannot be sent are counted and reported.

---

## What you need

| Part | Version |
|---|---|
| Server | the first release with table sensors, later than 3.44.0, with collector sessions and table connections on (both are on by default) |
| Managed collector (C#) | HSMDataCollector 4.1.0 (`4.1.0-preview` in the branch), .NET 6+ or .NET Framework 4.7.2+ |
| Native collector (C, C++) | 0.15.1, built with `HSM_COLLECTOR_TABLES=ON` and `HSM_COLLECTOR_HTTP=ON`, and libcurl 8.14.0 or later with WebSocket support |
| Legacy C++ wrapper | HSMCppWrapper 2.0.0 |
| Transport | **the collector session** (the default of the managed collector since 4.0.0 and of the wrapper since 2.0.0; the native collector needs `UseSessionTransport()`) |

Rows travel on a second WebSocket of the collector session, the **table connection**, which the collector opens by itself. **A collector on the HTTP transport cannot send rows**: its table sensors are `Unavailable`.

---

## The managed collector (C#)

### Creating a table sensor

```csharp
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://hsm.example.com",
    AccessKey = "<access key of the product>",
});

var options = new TableSensorOptions
{
    Description = "EURUSD quotes",
    SensorLocation = SensorLocation.Product, // the path starts at the product root
    KeepHistory = TimeSpan.FromDays(30),     // optional: null takes the folder's or product's setting
};
options.Columns.Add(TableColumn.Double("bid"));
options.Columns.Add(TableColumn.Double("ask"));
options.Columns.Add(TableColumn.Int64("volume"));

ITableSensor quotes = collector.CreateTableSensor("quotes/EURUSD", options);
```

- **Columns.** The time is implicit and first, named `time`: do not declare it. Declare at least one column with `TableColumn.Double(name)` or `TableColumn.Int64(name)`. Names are unique, 1 to 255 UTF-8 bytes, compared exactly; `time` is taken. The server allows 64 columns with the time (63 of yours).
- **Path.** As for every sensor: under `ComputerName/Module` by default (`SensorLocation.Module`), or at the product root with `SensorLocation.Product`. Empty parts and spaces around a part are removed (`quotes/ EURUSD` becomes `quotes/EURUSD`); `ITableSensor.Path` is the result. At most 1,024 UTF-8 bytes.
- **KeepHistory** is sent as you set it. The server counts it in whole weeks, rounded up (30 days are kept as 5 weeks), and rows may stay up to a week longer.
- A table sensor has no alerts, TTL, unit or previous paths: `TableSensorOptions` has none of these.
- **Create it once.** Creating the same path with the same columns again returns the same sensor. Another set of columns on the same path throws: another shape is another sensor (path).
- **What throws.** `CreateTableSensor` throws `ArgumentException` only for a mistake of the call: a null or empty path, a path with a backslash or a tab or longer than 1,024 bytes, no column, an invalid or duplicate name, a column named `time` ("The column name 'time' belongs to the time of the row, which every table sensor has first."), the path already created with other columns. It **never** throws because of the environment: a collector that cannot send tables gives a sensor in state `Unavailable` (below).
- `CreateTableSensor` is on `DataCollector`, on the interface `ITableSensorCollector`, and as an extension method on `IDataCollector` (which throws `NotSupportedException` for another implementation of `IDataCollector`).

You may create table sensors before or after `Start()`. Nothing of the feature exists (no thread, no compressor, no connection) until the first table sensor is created.

### Adding rows

```csharp
// One row: a time and one cell per column, in column order.
quotes.AddRow(DateTime.UtcNow, 1.0841, 1.0843, 100L);

// A cell without a value: TableCell.Null, or a nullable without a value.
double? noAsk = null;
quotes.AddRow(DateTime.UtcNow, 1.0841, noAsk, TableCell.Null);

// Many rows in one call: the cells row by row (row-major), all or nothing.
DateTime[] times = { t0, t1 };
TableCell[] cells = { 1.0841, 1.0843, 100L,
                      1.0842, 1.0844, 200L };
int taken = quotes.AddRows(times, cells, rowCount: 2);   // 2, or 0
```

- `AddRow` takes 1 to 4 cells directly, or a `TableCell[]` / `ReadOnlySpan<TableCell>` for any number. `AddRows` takes arrays (with a row count) or spans.
- `TableCell` converts by itself from `double`, `long` (and `int`), `double?` and `long?`. An integer cell is accepted by a Double column (converted); a double cell in an Int64 column is a mistake.
- **The time is yours.** Use the time the data belongs to (for a feed, the provider's time). Rows may come out of time order and times may repeat; the order of rows with the same time is kept. A `DateTime` of kind `Local` is converted to UTC, `Unspecified` is taken as UTC. Precision: 100 ns.
- Doubles travel bit for bit (NaN, infinities, -0.0).
- **Any thread.** Every member of `ITableSensor` may be called from any thread; adding a row allocates nothing.

**Return values.**

| Result | Meaning |
|---|---|
| `AddRow` returns `true`, `AddRows` returns the row count | The rows are in the collector's buffer and go out with the next tick. |
| `false` / `0` | The rows were **not** taken, and are counted in `RowsDropped` and reported: the collector is not running, the sensor is `Refused` or `Unavailable`, or the buffer is full. |
| `ArgumentException` | A row of the wrong shape: a cell count other than the column count, or a double cell in an Int64 column. Nothing else throws from `AddRow` and `AddRows`. |

`AddRows` takes all rows of the call or none.

### The tick

Once per **tick** (`CollectorOptions.TableTickPeriod`, 10 seconds by default, at least 1 second) the collector packs the rows of **all** its table sensors into one compressed message and sends it. A thousand table sensors make one message per tick. A row is therefore on the server about one tick after you added it. The collector splits a message that would break the server's limits by itself.

```csharp
var collectorOptions = new CollectorOptions
{
    ServerAddress = "https://hsm.example.com",
    AccessKey = key,
    TableTickPeriod = TimeSpan.FromSeconds(5),
    MaxTableBufferBytes = 64L * 1024 * 1024,   // rows waiting for the tick (the default)
    MaxTableBacklogBytes = 64L * 1024 * 1024,  // packed messages waiting for the server (the default)
};
```

### The state of a sensor

`ITableSensor.State`:

| State | Meaning | Rows |
|---|---|---|
| `Pending` | Its registration has not been answered yet (also after every `Start`, and while the server cannot be reached). | Kept in the buffer, within `MaxTableBufferBytes`. |
| `Registered` | The server took the registration. | Go out with every tick. |
| `Refused` | The server said no. **Final** for this collector. | Dropped and counted. |
| `Unavailable` | This collector cannot send tables. **Final** for this collector. | Dropped and counted. |

`ITableSensor.StateReason` says why a sensor is `Refused` or `Unavailable` (null otherwise), and the same text is logged once and sent to `Collector errors`. What we saw:

| State | `StateReason` |
|---|---|
| `Unavailable` | `table sensors need the collector session transport, and this collector sends over HTTP (CollectorOptions.Transport)` |
| `Refused` | `Table sensor 'quotes/EURUSD' is registered with the schema (time Time, bid Double, ask Double); a registration with (time Time, bid Double) is refused: a different shape is a different sensor.` |

A sensor is `Unavailable` when the collector uses the HTTP transport (also the automatic HTTP of .NET Framework with `AllowUntrustedServerCertificate`), a custom `CollectorOptions.DataSender`, the session of another collector created first from the same options, runs on a host where no session can exist, or is stopping or disposed. This is decided when the collector is constructed and stays for its life: **create the collector again** with a session of its own.

A sensor is `Refused` when the server refuses its registration, for example another schema or another kind of sensor on that path. Choose another path, or remove the sensor on the server and start the collector again.

While a sensor is `Registered`, the server may refuse the rows of one message for it. Then the rows are counted as lost, and: "the sensor is not on the server" puts it back to `Pending` (it registers again; a sensor removed on the site comes back empty); another schema or kind there makes it `Refused`; a sensor **blocked** on the site keeps it `Registered`, and its rows are lost until it is unblocked.

`RowsAccepted` counts the rows taken into the buffer, `RowsDropped` the rows lost, both since the sensor was created. An application can read all four members without the server.

### Lost rows and where they are reported

The collector never loses a row without counting it. Every lost row is counted in its sensor's `RowsDropped`, and at every tick:

- one line per reason goes to the collector's log and to the **`Collector errors`** sensor (added by `AddCollectorErrors()` or `AddCollectorMonitoringSensors()`), deduplicated like every collector error;
- the number of rows goes to the **`Queue overflow`** sensor (node `Collector queue stats`, added by `AddQueueOverflow()`), whose bar comment names the queue: `Table rows: 12`.

| Reason (log line) | When |
|---|---|
| not running: "Table rows were dropped: they were added while the collector was not running." | a row added before `Start`, after `Stop`, or during a stop |
| buffer full | the rows waiting for the tick reached `MaxTableBufferBytes` (8 bytes per cell, the time included: a row of time, bid, ask is 24 bytes) |
| "Table rows were dropped: they were added to a table sensor this collector cannot send (see its StateReason)." | the sensor is `Unavailable` |
| "Table rows were dropped: they were added to a table sensor the server refused." | the sensor is `Refused` |
| backlog full | packed messages waiting for the server reached `MaxTableBacklogBytes`: the oldest are dropped (never the one being sent) |
| refused by the server | the server refused the rows of a message |
| not delivered at stop | what the stop could not send in time |
| row too large, packing failed | a single row larger than what the server takes in one message; an internal fault of packing |
| buffer could not grow | out of memory |

At the end of `Stop`, one line says what the `Queue overflow` sensor did not get (losses of a collector without that sensor, or after its last value):

```
Collector stop: 1 table row(s) were lost during this run and are not in the Queue overflow sensor (not registered, or reported after it published its last bar).
```

### Start, stop, restart

- **Start.** Every table sensor goes back to `Pending` and registers again. The collector opens the table connection once the session is up and a registration was accepted, and logs `The table connection is up: frame limit 4194304 bytes, heartbeat 30 s.`
- **Stop.** From the moment `Stop` begins, new rows are refused and counted. The collector packs what it holds and sends it within the stop's time budget, beside the drain of the ordinary values, then closes the table connection before the session. What it could not send is counted as "not delivered at stop". Rows of a sensor that is still `Pending` at the stop are dropped and counted: wait for `Registered` before you rely on a short run.
- **Restart.** `Start` after `Stop` works; the collector uses a new sender identity per `Start`. Nothing is written to disk: rows held in memory are lost with the process (and counted, if the process stops normally).
- **Dispose** stops without sending, counts what it held, and never throws, from any state.

A message the server did not answer is sent again with the same identity; if the server had stored it, the server recognises the repeat and stores nothing twice.

### Memory

| Bound | Default | What happens past it |
|---|---|---|
| `MaxTableBufferBytes` | 64 MiB | rows waiting for the tick: new rows are refused ("buffer full") |
| `MaxTableBacklogBytes` | 64 MiB, about 16 million rows of time, bid, ask | packed messages waiting for the server (an outage): the oldest are dropped |

A quiet sensor keeps two buffers of 256 rows (4 KiB per column). A busy sensor's buffers follow its rows.

### .NET Framework

The same API works on .NET Framework 4.7.2. Two differences:

- With `AllowUntrustedServerCertificate` on `https`, .NET Framework cannot open a session at all, so table sensors are `Unavailable`. Use a trusted certificate.
- The `ReadOnlySpan` overloads need `System.Memory`. SDK-style projects generate the binding redirects; a classic project adds to its `app.config` redirects for `System.Memory` 4.0.2.0, `System.Buffers` 4.0.4.0, `System.Numerics.Vectors` 4.1.5.0, `System.Runtime.CompilerServices.Unsafe` 6.0.1.0 and `Microsoft.Bcl.AsyncInterfaces` 8.0.0.0. The array overloads need none of these types in your code.

The package carries `HSMTableBlocks.dll` and depends on `ZstdSharp.Port` (a managed zstd, no native binaries) and `System.IO.Hashing`.

### A complete program

This program sends 600 rows of time, bid and ask (one ask empty) and prints what happened. It was built against the branch's collector project and run against a server built from the branch.

```csharp
using System;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

// Sends 600 rows of time, bid, ask (one ask left empty) to the table sensor "quotes/EURUSD".
// Set HSM_KEY to an access key of the product; HSM_SERVER and HSM_PORT default to https://localhost and 443.
internal static class Program
{
    private static async Task<int> Main()
    {
        var key = Environment.GetEnvironmentVariable("HSM_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Set HSM_KEY to an access key of the product.");
            return 2;
        }

        var collector = new DataCollector(new CollectorOptions
        {
            ServerAddress = Environment.GetEnvironmentVariable("HSM_SERVER") ?? "https://localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("HSM_PORT") ?? "443"),
            AccessKey = key,
            ClientName = "table-quick-start",
            AllowUntrustedServerCertificate = true, // a test server's self-signed certificate
        });
        collector.AddCustomLogger(new ConsoleLogger());

        var options = new TableSensorOptions
        {
            Description = "EURUSD quotes (quick start)",
            SensorLocation = SensorLocation.Product, // the path starts at the product root
        };
        options.Columns.Add(TableColumn.Double("bid"));
        options.Columns.Add(TableColumn.Double("ask"));

        ITableSensor quotes = collector.CreateTableSensor("quotes/EURUSD", options);
        Console.WriteLine($"Created {quotes.Path}: {quotes.State} {quotes.StateReason}");

        await collector.Start();

        // Wait up to 30 s for the server to take the registration.
        for (var i = 0; i < 30 && quotes.State == TableSensorState.Pending; i++)
            await Task.Delay(1000);

        Console.WriteLine($"State: {quotes.State} {quotes.StateReason}");
        if (quotes.State != TableSensorState.Registered)
        {
            await collector.Stop();
            return 1;
        }

        // 600 rows over the last 10 minutes, one per second.
        var start = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 600; i++)
        {
            var bid = 1.0800 + 0.0010 * Math.Sin(i / 60.0);
            double? ask = i == 300 ? null : bid + 0.0002; // row 300 has no ask
            quotes.AddRow(start.AddSeconds(i), bid, ask);
        }

        await Task.Delay(TimeSpan.FromSeconds(15)); // longer than one tick (10 s)
        await collector.Stop();                     // sends what is still buffered

        Console.WriteLine($"Accepted {quotes.RowsAccepted}, dropped {quotes.RowsDropped}");
        collector.Dispose();
        return quotes.RowsDropped == 0 ? 0 : 1;
    }

    private sealed class ConsoleLogger : ICollectorLogger
    {
        public void Debug(string message) { }
        public void Info(string message) => Console.WriteLine($"info: {message}");
        public void Error(string message) => Console.WriteLine($"error: {message}");
        public void Error(Exception ex) => Console.WriteLine($"error: {ex.Message}");
    }
}
```

The project file, for building against the repository (a NuGet consumer references the `HSMDataCollector` package instead):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="<repository>\src\collector\HSMDataCollector\HSMDataCollector.csproj" />
    <ProjectReference Include="<repository>\src\api\HSMSensorDataObjects\HSMSensorDataObjects.csproj" />
    <ProjectReference Include="<repository>\src\api\HSMTableBlocks\HSMTableBlocks.csproj" />
  </ItemGroup>
</Project>
```

Its output against a server of the branch:

```
Created quotes/EURUSD: Pending
info: DataCollector (v. 4.1.0.0) -> Starting
info: DataCollector (v. 4.1.0.0) -> Running
info: The collector session is up: wss://127.0.0.1:18443/api/sensors/session, frame limit 4194304 bytes, heartbeat 30 s.
info: The table connection is up: frame limit 4194304 bytes, heartbeat 30 s.
State: Registered
info: DataCollector (v. 4.1.0.0) -> Stopping
info: DataCollector (v. 4.1.0.0) -> Stopped
Accepted 600, dropped 0
```

---

## The native collector (C and C++)

### Building with tables

Table sensors are in the native collector since 0.15.0 and are delivered since 0.15.1. The functions exist in every build, but a build without table support makes every table sensor `Unavailable` with the reason "built with HSM_COLLECTOR_TABLES=OFF". Build from `src/native/collector` with both options:

```bash
cmake -S src/native/collector -B build -DHSM_COLLECTOR_HTTP=ON -DHSM_COLLECTOR_TABLES=ON \
      -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake
cmake --build build --config Release
```

`HSM_COLLECTOR_TABLES=ON` turns on the manifest feature `tables` of `src/native/collector/vcpkg.json`, which brings zstd and zlib; `HSM_COLLECTOR_HTTP=ON` brings libcurl with WebSocket support (8.14.0 or later is needed for the session). The port `hsm-collector` of the repository's vcpkg registry is at 0.11.1 and has no table support yet. The HSM agent and the Linux agent are built without tables.

### Rules that differ from C#

- **Use the session.** Call `hsm_collector_use_session_transport` (C) or `Collector::UseSessionTransport()` (C++) before Start. Without it (the default in-memory sender, or `use_http_transport`) table sensors are `UNAVAILABLE` with the reason.
- **Time** is Unix nanoseconds (`int64`, since 1970-01-01 UTC), kept to 100 ns (rounded down). The C++ wrapper takes a `std::chrono::system_clock::time_point`.
- **`Unavailable` is decided again at every Start**: a sensor `Unavailable` in one run can be `Pending` in the next. `Refused` stays final.
- **Cells (C).** A cell is a raw 8-byte union, read by the column's type: a double put into an Int64 column is not detected. The C++ `TableCell` detects it and throws. An empty cell is a bit in the `nulls` map: bit `c` for `add_row`, bit `r * columns + c` for `add_rows`.
- A table the server's limits can never take (a path or column count beyond what the server states) becomes `Unavailable` as soon as the limits are known.
- Create on a disposed collector returns `HSM_RESULT_INVALID_STATE`.
- The settings `hsm_table_options_t` / `TableOptions` (`tick_period` 10 s, at least 1 s; `max_buffer_bytes` and `max_backlog_bytes` 64 MiB) are set before the first table sensor.

### C

The functions are in `hsm_collector/hsm_collector.h`, section "Table sensors"; the reference is [`docs/native-collector-c-abi.md`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docs/native-collector-c-abi.md#table-sensors-0150-delivered-since-0151).

| Function | Does |
|---|---|
| `hsm_collector_set_table_options` | the tick and the two bounds; before the first table sensor |
| `hsm_collector_create_table_sensor(collector, path, columns, count, options, &sensor)` | creates (or returns a new handle to) a table sensor |
| `hsm_table_sensor_add_row(sensor, time_ns, cells, count, nulls)` | one row |
| `hsm_table_sensor_add_rows(sensor, times_ns, cells, nulls, rows, &taken)` | many rows, all or nothing |
| `hsm_table_sensor_get_stats` | state, rows accepted, rows dropped |
| `hsm_table_sensor_state_reason` | why `REFUSED` or `UNAVAILABLE` |
| `hsm_table_sensor_path` | the normalized full path |
| `hsm_table_sensor_release` | frees the handle (the sensor stays in the collector; safe before or after the collector is destroyed) |

Result codes of the add calls: `HSM_RESULT_OK` taken; `HSM_RESULT_INVALID_ARGUMENT` a wrong shape (nothing counted); `HSM_RESULT_INVALID_STATE` not running, destroyed, `REFUSED` or `UNAVAILABLE` (counted); `HSM_RESULT_LIMIT_EXCEEDED` buffer full (counted). The add calls never set the collector's last error.

```c
#include <hsm_collector/hsm_collector.h>

#include <stdio.h>
#include <time.h>
#ifdef _WIN32
#include <windows.h>
static void sleep_ms(int ms) { Sleep((DWORD)ms); }
#else
static void sleep_ms(int ms) { struct timespec t = { ms / 1000, (ms % 1000) * 1000000L }; nanosleep(&t, NULL); }
#endif

static int64_t now_unix_ns(void)
{
    struct timespec now;
    timespec_get(&now, TIME_UTC);
    return (int64_t)now.tv_sec * 1000000000 + now.tv_nsec;
}

/* Sends 600 rows (time, bid, ask) to the table sensor "quotes/EURUSD"; the ask of row 300 is empty. */
int main(void)
{
    hsm_collector_options_t options = { 0 };
    options.access_key = "<access key of the product>";
    options.server_address = "https://localhost";
    options.port = 443;
    options.allow_untrusted_server_certificate = true; /* a test server's self-signed certificate */

    hsm_collector_t* collector = NULL;
    if (hsm_collector_create(&options, &collector) != HSM_RESULT_OK)
        return 1;

    /* Table sensors need the collector session. Call before Start. */
    if (hsm_collector_use_session_transport(collector) != HSM_RESULT_OK)
    {
        hsm_collector_destroy(collector);
        return 1;
    }

    hsm_table_column_t columns[] = { { "bid", HSM_TABLE_COLUMN_DOUBLE }, { "ask", HSM_TABLE_COLUMN_DOUBLE } };
    hsm_table_sensor_options_t sensor_options = hsm_table_sensor_options_default();
    sensor_options.description = "EURUSD quotes";
    sensor_options.sensor_location = 1; /* 1 = Product: the path starts at the product root */

    hsm_table_sensor_t* quotes = NULL;
    if (hsm_collector_create_table_sensor(collector, "quotes/EURUSD", columns, 2, &sensor_options, &quotes) != HSM_RESULT_OK)
    {
        hsm_collector_destroy(collector);
        return 1;
    }

    hsm_collector_start(collector);

    /* Wait up to 30 s for the server to take the registration. */
    hsm_table_sensor_stats_t stats = { sizeof stats };
    for (int i = 0; i < 30; i++)
    {
        hsm_table_sensor_get_stats(quotes, &stats);
        if (stats.state != HSM_TABLE_SENSOR_PENDING)
            break;
        sleep_ms(1000);
    }

    /* 600 rows, one a second, over the last 10 minutes. */
    int64_t start = now_unix_ns() - 600LL * 1000000000;
    for (int i = 0; i < 600; i++)
    {
        hsm_table_cell_t row[2];
        row[0].f64 = 1.0800 + 0.0001 * (i % 20);
        row[1].f64 = row[0].f64 + 0.0002;
        uint8_t nulls = (i == 300) ? 0x02 : 0x00; /* bit 1 = column 1 (ask) has no value */

        if (hsm_table_sensor_add_row(quotes, start + (int64_t)i * 1000000000, row, 2, &nulls) != HSM_RESULT_OK)
            fprintf(stderr, "row %d was dropped (counted on the sensor)\n", i);
    }

    sleep_ms(15000);               /* longer than one tick (10 s) */
    hsm_collector_stop(collector); /* sends what is still buffered, within the stop budget */

    char reason[256];
    hsm_table_sensor_get_stats(quotes, &stats);
    hsm_table_sensor_state_reason(quotes, reason, sizeof reason);
    printf("state %d, accepted %llu, dropped %llu %s\n", (int)stats.state,
        (unsigned long long)stats.rows_accepted, (unsigned long long)stats.rows_dropped, reason);

    hsm_table_sensor_release(quotes);
    hsm_collector_destroy(collector);
    return 0;
}
```

States: `HSM_TABLE_SENSOR_PENDING` 0, `REGISTERED` 1, `REFUSED` 2, `UNAVAILABLE` 3.

### C++

[`hsm_collector/tables.hpp`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/native/collector/include/hsm_collector/tables.hpp), included by `hsm_collector/hsm_collector.hpp`: `Collector::CreateTableSensor(path, TableSensorOptions)` returns a move-only `TableSensor` with `AddRow(time_point, {cells})`, `AddRows`, `Stats()` (state, reason, rows accepted, rows dropped) and `Path()`. A `TableCell` is a `double`, an `int64_t`, an `std::optional` of either, or `TableCell::Null()`. `AddRow` returns `false` for a row that was dropped and counted, and throws `hsm::collector::Error` only for a wrong shape.

```cpp
#include <hsm_collector/hsm_collector.hpp>

#include <chrono>
#include <iostream>
#include <optional>
#include <thread>

using namespace std::chrono_literals;
namespace hc = hsm::collector;

// Sends 600 rows (time, bid, ask) to the table sensor "quotes/EURUSD"; the ask of row 300 is empty.
int main()
{
    try
    {
        hc::CollectorOptions options;
        options.access_key = "<access key of the product>";
        options.server_address = "https://localhost";
        options.port = 443;
        options.allow_untrusted_server_certificate = true; // a test server's self-signed certificate

        hc::Collector collector(options);
        collector.UseSessionTransport(); // table sensors need the collector session; before Start

        hc::TableSensorOptions sensor_options;
        sensor_options.columns = { hc::TableColumn::Double("bid"), hc::TableColumn::Double("ask") };
        sensor_options.description = "EURUSD quotes";
        sensor_options.location = hc::SensorLocation::Product; // the path starts at the product root

        hc::TableSensor quotes = collector.CreateTableSensor("quotes/EURUSD", sensor_options);
        collector.Start();

        for (int i = 0; i < 30 && quotes.Stats().state == hc::TableSensorState::Pending; i++)
            std::this_thread::sleep_for(1s);

        const auto start = std::chrono::system_clock::now() - 10min;
        for (int i = 0; i < 600; i++)
        {
            const double bid = 1.0800 + 0.0001 * (i % 20);
            const std::optional<double> ask = i == 300 ? std::nullopt : std::optional<double>(bid + 0.0002);
            if (!quotes.AddRow(start + std::chrono::seconds(i), { bid, ask }))
                std::cerr << "row " << i << " was dropped (counted on the sensor)\n";
        }

        std::this_thread::sleep_for(15s); // longer than one tick (10 s)
        collector.Stop();                 // sends what is still buffered, within the stop budget

        const auto stats = quotes.Stats();
        std::cout << "state " << static_cast<int>(stats.state) << ", accepted " << stats.rows_accepted
                  << ", dropped " << stats.rows_dropped << ' ' << stats.reason << '\n';
        return 0;
    }
    catch (const hc::Error& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
```

Lost rows are reported as in C#: through the logger and `.module/Collector errors`, and to the one `.module/Collector queue stats/Queue overflow` sensor (the native collector does not split it by queue), with the same stop line.

---

## The legacy C++ wrapper (HSMCppWrapper)

The wrapper's own API has no table type. Since 2.0.0 the wrapper runs on the native collector, built with tables, and gives access to it through `DataCollectorProxy::Native()`:

```cpp
#include "HSMCppWrapper.h"
#include <hsm_collector/hsm_collector.hpp>   // needed for Native()

hsm_wrapper::DataCollectorProxy proxy(key, "https://hsm.example.com", 443, "MyModule");

hsm::collector::TableSensorOptions options;
options.columns = { hsm::collector::TableColumn::Double("bid"), hsm::collector::TableColumn::Double("ask") };
auto quotes = proxy.Native().CreateTableSensor("quotes/EURUSD", options);

proxy.Start();
quotes.AddRow(std::chrono::system_clock::now(), { 1.0841, 1.0843 });
```

- The wrapper uses the **collector session by default** (chosen when the proxy is constructed), so a table sensor made through `Native()` before or after `Start` registers and sends like any native one. Before `Start` its state is `Pending`.
- `proxy.UseHttpTransport()` (new in 2.0.0, for a server without sessions, before 3.43.0) switches the whole wrapper to HTTP. Every table sensor is then `Unavailable` with the reason "table sensors need the collector session transport (hsm_collector_use_session_transport)", and its rows are dropped and counted.
- The wrapper's bundle ships `zstd.dll` beside `libcurl.dll` and `z.dll`.

---

## The transport and the server

| Collector on | Against | What you see |
|---|---|---|
| the session | a server with table sensors | `Registered`, rows on the server one tick later |
| HTTP (`CollectorTransport.Http`, `UseHttpTransport()`) | any server | `Unavailable`; rows dropped and counted |
| the session | a server with sessions but without table sensors (3.43.0, 3.44.0) | the ordinary values flow; the registration of a table sensor is refused or never answered, and no row is delivered; the collector says once that the server offers no table connection |
| the session | a server without sessions (before 3.43.0) | no session comes up: the collector sends nothing at all, values included |
| the session | a server with table connections switched off (`CollectorSessions.MaxTableFrameBytes = 0`) | sensors register, the table connection is refused (404); the collector asks again every 5 minutes; messages wait in the backlog (oldest dropped past `MaxTableBacklogBytes`) |

Collectors that share one access key and send large messages (over 64 KiB) take turns on the server; one that waits too long is closed by the server and sends the same message again. Collectors that see this often need keys of their own: the server's log names the key.

---

## When something goes wrong

| Symptom | Look at | Likely cause |
|---|---|---|
| `State` stays `Pending` | the collector log: "The collector session is up" missing | the server cannot be reached, the key is wrong, or the server has no sessions |
| `State` is `Unavailable` | `StateReason` | HTTP transport, a custom `DataSender`, .NET Framework with an untrusted certificate, a native build without tables or session |
| `State` is `Refused` | `StateReason` | another schema or kind on that path on the server |
| `AddRow` returns `false` | `RowsDropped`, `Collector errors` | not started yet, buffer full, sensor refused or unavailable |
| `Registered`, no rows on the site | `.tables/Tables refused` and `.tables/Rows` in the product; the server's sensors `Database/Tables/Write failures`, `Tables refused` | the server refused the rows (a sensor blocked or removed on the site), or cannot store (disk below `MinFreeDiskMb`) |
| Rows late | `.tables/Delivery lag` | the tick, a slow network, or the sender's clock |
| `Queue overflow` shows `Table rows: N` | `Collector errors` | the reason per line, above |

---

## Limits and known limits

- **Memory only.** Rows and packed messages live in memory: a crash loses what is held, and is not counted. A file of undelivered rows is planned and not built.
- **A stop is bounded.** What is not sent within the stop's budget is counted as lost. A message the server stored but whose answer came after the budget is counted as lost too (the count errs on the side of loss).
- **Server limits lowered while a message was in flight.** Rare: when an answer was lost and the server's next connection states smaller limits, the managed collector sends those rows again in smaller messages and the server may store them twice; the native collector counts them as lost although the server may hold them. Neither loses a row without a count.
- **More than 100 refused tables in one message.** The server lists at most 100; rows of the rest are reported in the log, not in each sensor's `RowsDropped`.
- **Waiting for the table connection.** A message waits at most 15 s per attempt and three connections, then it is "not delivered" and goes again with the next tick.
- **A logger that disposes the collector** from inside the stop line deadlocks (managed); a native logger must not call start, stop or dispose from lifecycle lines.
- **Native on Windows with TLS:** a stop can wait for a large message the server stopped reading, longer than the stop budget (a libcurl limit; open).
- **Native:** no run against a real server is part of 0.15.1's tests; it is tested against a second, independent test server.

---

## Try it

From an empty server to rows sent by your own C# program, on the chart and in a downloaded file. You need Docker Desktop, the .NET 8 SDK and a clone of the repository with the branch `feature/1842-table-sensors`.

**1. Run a server from the branch and create a product.** Follow steps 1 to 4 of [Table Sensors: Try it](Table-Sensors#try-it): a server at `https://localhost` (or your own port), a product `TableDocs`, its `DefaultKey`.

**2. Make the program.** Create a folder with the [project file](#a-complete-program) (set `<repository>` to your clone) and the program above as `Program.cs`. `dotnet build -c Release`. Expect "Build succeeded" (warnings `CA1416` of the collector project are expected). Failure: a missing reference (check the three paths).

**3. Run it.**

```powershell
$env:HSM_KEY = "<the key>"
$env:HSM_SERVER = "https://localhost"; $env:HSM_PORT = "443"   # or your own port
dotnet run -c Release --no-build
```

Expect the output shown under [A complete program](#a-complete-program), ending in `Accepted 600, dropped 0`, exit code 0. Failures:

| Output | Meaning |
|---|---|
| `State: Pending` after 30 s, exit code 1; no "collector session is up" line | the server is not reachable at that address and port, or the key is wrong |
| `State: Refused Table sensor 'quotes/EURUSD' is registered with the schema …` | that path already has other columns: change the path |
| `State: Unavailable …` | the collector cannot send tables: the text after the state says why |
| `Accepted 600, dropped N` with N > 0 and an `error:` line | the rows were lost; the line says why |

**4. Look at the rows.** On the site select `TableDocs/quotes/EURUSD`. Expect the schema (`time`, `bid`, `ask`), `600 rows, the first at …, the last at … UTC.`, `1 row had no value in ask.` and two lines (with 600 rows in the last hour the chart draws the rows themselves). **Download CSV** gives `time,bid,ask` and 600 lines; line 302 (row 300) ends with a comma.

**5. Read them over the REST API**, as in [Table Sensors](Table-Sensors#reading-rows-over-the-rest-api): `X-Hsm-Row-Count: 600`.

**6. See a failure on purpose.** Remove the line that adds the `ask` column and run again: the log says `error: The server refused the table sensor quotes/EURUSD: Table sensor 'quotes/EURUSD' is registered with the schema (time Time, bid Double, ask Double); a registration with (time Time, bid Double) is refused: …`, the program prints `State: Refused …` and exits with 1. Put the line back and add `Transport = CollectorTransport.Http` to the collector's options: the log says `error: The table sensor quotes/EURUSD cannot send its rows: table sensors need the collector session transport, and this collector sends over HTTP (CollectorOptions.Transport). Its rows are dropped and counted.` already at `CreateTableSensor`, and the program prints `Created quotes/EURUSD: Unavailable …`.
