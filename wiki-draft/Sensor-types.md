# Sensor Types

> **Draft, not released yet.** This page describes the table sensors branch (`feature/1842-table-sensors`, epic #1842), whose server is later than 3.44.0. Everything except the [Table](#table) section is also true of the released 3.44.0. It replaces the short published page when the branch is released.

Every sensor in HSM has a **kind** (its type). The kind decides what one value is, how the site shows it, which alerts it can have and how a collector sends it. The server knows **12 kinds**. Anything else you meet in a collector (rate sensors with a period, bar sensors with a period, "last value" sensors, function sensors) is a **convenience of the collector**: it computes values and sends them as one of the 12 kinds.

---

## The kinds at a glance

| Kind | Number | One value is | On the sensor page | Managed collector (C#) | Alerts |
|---|---|---|---|---|---|
| Boolean | 0 | `true` or `false` | Graph (points), Table, Journal | `CreateBoolSensor` | Status, Comment, New data, Inactivity Period |
| Integer | 1 | a 32-bit integer | Graph (line), Table, Journal | `CreateIntSensor` | + Value, EMA (Value) |
| Double | 2 | a double | Graph (line), Table, Journal | `CreateDoubleSensor` | + Value, EMA (Value) |
| String | 3 | a text | Table, Journal (no Graph) | `CreateStringSensor` | + Value (text), Value length |
| IntegerBar | 4 | min, max, mean, count, first and last of integers over a period | Graph (candlesticks or mean ± sigma), Table, Journal | `CreateIntBarSensor`, `Create1MinIntBarSensor`, ... | + Min, Max, Mean, Count, First value, Last value, EMA of Min, Max, Mean and Count |
| DoubleBar | 5 | the same of doubles | the same | `CreateDoubleBarSensor`, `Create5MinDoubleBarSensor`, ... | the same |
| File | 6 | a file: bytes, a name and an extension | Files (Download, Preview), Journal | `CreateFileSensor`, `SendFileAsync` | + Size |
| TimeSpan | 7 | a duration | Graph (line), Table, Journal | `CreateTimeSensor` | + Value |
| Version | 8 | a version number such as `3.44.0` | Graph, Table, Journal | `CreateVersionSensor` | + Value (text operations) |
| Rate | 9 | a double: a count per second (or per minute, hour, ...) | Graph (line), Table, Journal | `CreateRateSensor`, `CreateM1RateSensor`, `CreateM5RateSensor` | + Value, EMA (Value) |
| Enum | 10 | an integer code with a name and a colour | Graph (coloured points), Table, Journal | `CreateEnumSensor` | + Value (the code), EMA (Value) |
| Table | 11 | not a value: rows of a time and typed columns | Schema, chart of rows, CSV download | `CreateTableSensor` | **none** (alerts go on its feed sensors) |

"Number" is the value of the kind in the API (`SensorType` in a registration). "+" means: in addition to the four alert properties every kind but Table has.

Where the code is: the kinds are the enum `SensorType` in [HSMSensorDataObjects](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/api/HSMSensorDataObjects/SensorType.cs); the managed collector's methods are on [`IDataCollector`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/collector/HSMDataCollector/Core/IDataCollector.cs) (`CreateTableSensor` is an extension method on it, see [DataCollector Table Sensors](DataCollector-Table-Sensors)); the native C++ collector's are on `hsm::collector::Collector` in [`collector.hpp`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/native/collector/include/hsm_collector/collector.hpp) and in the C header [`hsm_collector.h`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/native/collector/include/hsm_collector/hsm_collector.h).

---

## What every kind has (except Table)

Every value of the kinds 0 to 10 carries, besides the value itself:

| Field | Meaning |
|---|---|
| Time | When the value was measured, in UTC. The collector sets it to the moment of `AddValue` unless you pass one. |
| Receiving time | When the server received it. |
| Status | `Ok` or `Error`. A collector may also send `Warning`; the server stores it as `Error`. |
| Comment | A text. The server removes HTML from it except `markdown` and `br`, and masks what looks like a credential. |

The sensor page of these kinds has the tabs **Graph** (not for String or File), **Table** and **Journal**. Bars also have a **Bars count** box (1 to 1,000).

**History.** The server keeps a sensor's values for the period of its **Keep sensor history** setting (on the sensor page under "Cleanup": 1 week, 1 month, 3 months, 6 months, 1 year, 5 years, Forever or Custom). A sensor takes the setting of its folder or product unless you set its own ("From parent (1 month)"). A new product starts with 1 month. Older values are removed by the server's cleanup. **Remove sensor after inactivity** removes a sensor that has sent nothing for that long (a new product starts with 1 month).

**Alerts.** Every kind but Table can have alerts on these properties:

| Property (as the editor names it) | Operations |
|---|---|
| Status | is changed, is changed to Ok, is changed to Error, is Ok, is Error |
| Comment | equals, not equals, contains, starts with, ends with, is changed |
| New data | received a new value |
| Inactivity Period | no value for longer than the period (the TTL) |

The properties of each kind are added in the sections below. Numeric properties take `<`, `<=`, `>`, `>=`, `==`, `!=`; text properties take equals, not equals, contains, starts with, ends with. An alert has at most 10 conditions. **EMA** properties are offered for Integer, Double, Rate, Enum and both bars; they have values only while the sensor's EMA statistics are switched on. The EMA keeps 0.9 of the old value and adds 0.1 of the new one. See [Alerts Overview](Alerts-Overview) and the [Conditions Reference](Alert-Conditions-Reference).

**Paths.** A sensor path has at most 10 parts (`a/b/c` has 3). Empty parts, spaces around a part and a leading `/` are dropped (`/a// b` is stored as `a/b`); a path may not contain `\` or a tab.

**Sending without a collector.** Each kind 0 to 10 has its own route in the Sensor API: `POST api/sensors/bool`, `int`, `double`, `string`, `timespan`, `version`, `rate`, `enum`, `intBar`, `doubleBar`, `file`, and `list` for several values at once. See [REST API](REST-API). Table rows have no such route: they are sent by a collector (see [Table](#table)).

---

## Boolean

One value is `true` or `false`. The Graph tab draws it as points.

```csharp
collector.CreateBoolSensor("service/healthy").AddValue(true);
```

Native C++: `Collector::CreateBoolSensor`; C: `hsm_collector_create_bool_sensor`, `hsm_sensor_add_bool`.

Alerts: only the common properties (Status, Comment, New data, Inactivity Period).

## Integer

One value is a 32-bit signed integer. The Graph tab draws a line with points.

```csharp
collector.CreateIntSensor("orders/open").AddValue(42);
```

Native C++: `Collector::CreateIntSensor`; C: `hsm_collector_create_int_sensor`, `hsm_sensor_add_int`.

Alerts: Value, EMA (Value).

## Double

One value is a double. The Graph tab draws a line.

```csharp
collector.CreateDoubleSensor("latency/ms").AddValue(12.5);
```

Native C++: `Collector::CreateDoubleSensor` (and `CreateMetricSensor`); C: `hsm_collector_create_double_sensor`, `hsm_sensor_add_double`.

Alerts: Value, EMA (Value).

## String

One value is a text. There is no Graph tab: the values are in the Table tab. The server removes HTML from the text except `markdown` and `br`, and masks what looks like a credential.

```csharp
collector.CreateStringSensor("build/branch").AddValue("release/3.44", SensorStatus.Ok, "deployed");
```

Native C++: `Collector::CreateStringSensor`; C: `hsm_collector_create_string_sensor`, `hsm_sensor_add_string`.

Alerts: Value (text operations), Value length (numeric).

## TimeSpan

One value is a duration. The Graph tab draws a line.

```csharp
collector.CreateTimeSensor("jobs/last duration").AddValue(TimeSpan.FromSeconds(95));
```

Native C++: `Collector::CreateTimeSpanSensor` (`AddTicks`, or `AddValue` with a `std::chrono` duration); C: `hsm_collector_create_timespan_sensor`, `hsm_sensor_add_timespan`.

Alerts: Value (numeric).

## Version

One value is a version number of two to four parts; a version whose fourth part is 0 is shown with three (`3.44.0`).

```csharp
collector.CreateVersionSensor("app/version").AddValue(new Version(3, 44, 0));
```

Native C++: `Collector::CreateVersionSensor`; C: `hsm_collector_create_version_sensor`, `hsm_sensor_add_version`.

Alerts: Value (text operations: equals, not equals, contains, starts with, ends with).

## Rate

One value is a double: a number of events per second (or per minute, hour, day, week or month: the sensor's unit, shown on the Graph axis). A rate sensor of a collector adds up what you give it and sends, once per period, the sum divided by the seconds that passed.

```csharp
IMonitoringRateSensor requests = collector.CreateM1RateSensor("api/requests");
requests.AddValue(1); // once per request: the sensor sends the sum per second each minute
```

`CreateRateSensor(path, RateSensorOptions)` sets the period yourself (1 minute by default); `CreateM1RateSensor` and `CreateM5RateSensor` send every 1 and 5 minutes.

Native C++: `Collector::CreateRateSensor`, `CreateM1RateSensor`, `CreateM5RateSensor`; C: `hsm_collector_create_rate_sensor`, `hsm_sensor_add_rate`.

Alerts: Value, EMA (Value). There is no kind "Counter": the collector's files named "counter" are rate sensors.

## Enum

One value is an integer code. The sensor's registration names each code (a name, a description and a colour), and the Graph tab draws the values as points in those colours.

```csharp
var states = new EnumSensorOptions
{
    EnumOptions = new List<EnumOption>
    {
        new EnumOption(0, "Idle", "Nothing to do", System.Drawing.Color.Gray),
        new EnumOption(1, "Busy", "Processing", System.Drawing.Color.Green),
    },
};
collector.CreateEnumSensor("worker/state", states).AddValue(1);
```

Native C++: `Collector::CreateEnumSensor`; C: `hsm_collector_create_enum_sensor`, `hsm_sensor_add_enum`.

Alerts: Value (the code, numeric), EMA (Value).

## IntegerBar and DoubleBar

One value is a **bar**: the minimum, maximum, mean, count, first and last of the values added during a period (5 minutes by default), with the period's open and close time. A collector also sends the standard deviation; the Graph tab can draw a bar as a candlestick (the default) or as the mean with ± one standard deviation. While a period is open, the collector sends the bar so far every 15 seconds by default; the server merges the sends of one bar by its open time.

```csharp
IBarSensor<int> queue = collector.Create1MinIntBarSensor("queue/length");
queue.AddValue(17);

IBarSensor<double> spread = collector.Create5MinDoubleBarSensor("quotes/spread", precision: 5);
spread.AddValue(0.00012);
```

Bars with other periods: `CreateIntBarSensor(path, barPeriod, postPeriod)` and `CreateDoubleBarSensor(...)` (milliseconds), or the ready-made 1, 5, 10, 30 minute and 1 hour forms (`Create1MinIntBarSensor` ... `Create1HrDoubleBarSensor`). `AddPartial(min, max, mean, first, last, count)` adds a bar you computed yourself.

Native C++: `Collector::CreateIntBarSensor`, `CreateDoubleBarSensor` (`AddValue`, `AddPartial`); C: `hsm_collector_create_int_bar_sensor`, `hsm_collector_create_double_bar_sensor`, `hsm_sensor_add_bar_int`, `hsm_sensor_add_bar_double`.

Alerts: Min, Max, Mean, Count, First value, Last value, and EMA of Min, Max, Mean and Count. Bars are never aggregated.

## File

One value is a file: its bytes, a name and an extension. The sensor page has the tabs **Files** and **Journal**: each value can be downloaded (**Download**) or opened (**Preview**); a `csv` file under 5 MB or a `txt` file under 1 MB is also shown inline. The server stores the bytes compressed when that makes them smaller.

```csharp
IFileSensor report = collector.CreateFileSensor("reports/daily", "report", "csv");
await report.SendFile(@"C:\reports\daily.csv");               // the file's own name and extension
report.AddValue("time,value\n2026-11-26T00:00:00Z,1\n");        // text sent as report.csv
```

`collector.SendFileAsync(sensorPath, filePath)` does the same without keeping a sensor object.

Limits:

- The managed collector refuses a file larger than `FileSensorOptions.MaxFileSizeBytes`: 10 MB by default, at most 128 MB.
- On the collector session (the default transport since collector 4.0.0) a file travels inside one frame of the server, where each byte takes up to four characters: with the server's default frame of 4 MiB a file of about 1 MiB fits. A larger file is dropped, logged and counted in the collector's `Queue overflow` sensor, unless the server's `CollectorSessions.MaxFrameBytes` is raised. Over the HTTP transport the server takes a request body of up to about 50 MB.
- File values stay in the ordinary history store: they are never sealed (see [Sealed Sensor History](Sealed-Sensor-History)).

Native C++: `Collector::CreateFileSensor` (`AddContent`, `SendFile`); C: `hsm_collector_create_file_sensor`, `hsm_sensor_add_file`, `hsm_sensor_add_file_from_path`.

Alerts: Size (the size of the file, numeric).

> **Large file values (dumps)** are not part of this page: they are described by the pages of epic #1882.

## Table

A table sensor does not hold values. Its data is **rows**: each row has a time and a fixed set of columns of 64-bit integers or doubles, and any cell but the time may be empty. A collector registers the sensor with its columns and sends rows in bulk, tens of thousands an hour if needed. The sensor page shows the columns, a chart of the rows of a time range and a **Download CSV** button.

```csharp
var options = new TableSensorOptions { SensorLocation = SensorLocation.Product };
options.Columns.Add(TableColumn.Double("bid"));
options.Columns.Add(TableColumn.Double("ask"));
ITableSensor quotes = collector.CreateTableSensor("quotes/EURUSD", options);
quotes.AddRow(DateTime.UtcNow, 1.0841, 1.0843);
```

A table sensor has no status, no alerts, no Inactivity Period, and cannot be moved or renamed. Its rows are kept by whole weeks. It needs the collector session: a collector on HTTP cannot send rows.

- For a user of the site: [Table Sensors](Table-Sensors).
- For a developer of a collector (C#, C, C++): [DataCollector Table Sensors](DataCollector-Table-Sensors).

Native C++: `Collector::CreateTableSensor` in [`tables.hpp`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/native/collector/include/hsm_collector/tables.hpp); C: `hsm_collector_create_table_sensor`, `hsm_table_sensor_add_row`.

---

## Conveniences of the collector

These methods do not make a kind of their own. They send the kind in brackets.

| Method (managed) | Sends | What it does |
|---|---|---|
| `CreateLastValueBoolSensor`, `...IntSensor`, `...DoubleSensor`, `...StringSensor`, `...VersionSensor`, `...TimeSpanSensor`, `CreateLastValueSensor<T>` | the plain kind of `T` | keeps the last value you gave it and sends it periodically |
| `CreateFunctionSensor<T>(path, Func<T>)` | the plain kind of `T` (bool, int, double, string, TimeSpan or Version) | calls your function once per period and sends the result |
| `CreateValuesFunctionSensor<T, U>(path, Func<List<U>, T>)` | the plain kind of `T` | collects the values you add and gives the list to your function once per period |
| `CreateM1RateSensor`, `CreateM5RateSensor` | Rate | a rate sensor with a period of 1 or 5 minutes |
| `Create1MinIntBarSensor` ... `Create1HrDoubleBarSensor` | IntegerBar or DoubleBar | a bar sensor with that period |
| `CreateServiceCommandsSensor` | String | the collector's own commands log |

```csharp
IDataCollector collector = new DataCollector(options);
collector.CreateFunctionSensor("threads", () => System.Diagnostics.Process.GetCurrentProcess().Threads.Count);
collector.CreateLastValueDoubleSensor("price/last", 0.0).AddValue(1.0841);
```

The native collector has last-value sensors for bool, int, double and string, and function sensors for `int` only.

Default sensors (CPU, memory, disks, Windows information, collector diagnostics) are also ordinary sensors of these kinds: see [HSMDataCollector](HSMDataCollector), [Windows sensors](Windows-sensors-collection) and [Unix sensors](Unix-sensors-collection).
