## Instant sensor
Full setup example for a instant sensor

```C#
var collector = new DataCollector(productKey);

var sensorOptions = new InstantSensorOptions()
{
    Description = "tests",
    SensorUnit = Unit.KB,

    KeepHistory = TimeSpan.FromDays(31),
    SelfDestroy = TimeSpan.FromDays(31),

    EnableForGrafana = true,
    AggregateData = true,

    TtlAlert = AlertFactory.IfInactivityPeriodIs(TimeSpan.FromMinutes(15)).ThenSetIcon("🎃").AndNotify("Time is over").Build(),

    Alerts = new List<InstantAlertTemplate>
    {
        AlertFactory.IfValue(AlertOperation.GreaterThan, 5).ThenNotify("$product $path test").AndSetIcon("🤣").AndSetSensorError().Build(),
        AlertFactory.IfComment(AlertOperation.IsChanged).ThenNotify("$product $path comment is changed").AndSetIcon("🎃").BuildAndDisable(),

        AlertFactory.IfValue(AlertOperation.GreaterThan, 5).AndValue(AlertOperation.LessThanOrEqual, 20).ThenSetIcon("Sds").BuildAndDisable(),
    }
};

var sensor = collector.CreateDoubleSensor("testSettings/testAlerts22222", sensorOptions);

await collector.Start();
```

or just set a description of a sensor

```C#
var collector = new DataCollector(productKey);

var sensor = collector.CreateDoubleSensor("testSettings/testAlerts22222", "test description");

await collector.Start();
```

## Bar sensor
Full setup example for a bar sensor

```C#
var collector = new DataCollector(productKey);

var sensorOptions = new BarSensorOptions()
{
    Description = "tests",
    SensorUnit = Unit.MB,

    PostDataPeriod = TimeSpan.FromSeconds(15), // how often to send the current bar
    BarTickPeriod = TimeSpan.FromSeconds(5), // how often to update the value of the current bar
    BarPeriod = TimeSpan.FromMinutes(5), // timeframe of the current bar

    KeepHistory = TimeSpan.FromDays(31),
    SelfDestroy = TimeSpan.FromDays(31),

    EnableForGrafana = true,
    AggregateData = true,

    TtlAlert = AlertFactory.IfInactivityPeriodIs(TimeSpan.FromMinutes(15)).ThenSetIcon("🎃").AndNotify("Time is over").Build(),

    Alerts = new List<BarAlertTemplate>
    {
        AlertFactory.IfMean(AlertOperation.GreaterThan, 5).ThenNotify("$product $path test").AndSetIcon("🤣").AndSetSensorError().Build(),
        AlertFactory.IfBarComment(AlertOperation.IsChanged).ThenNotify("$product $path comment is changed").AndSetIcon("🎃").BuildAndDisable(),

        AlertFactory.IfMax(AlertOperation.GreaterThan, 5).AndMax(AlertOperation.LessThanOrEqual, 20).ThenSetIcon("Sds").BuildAndDisable(),
    }
};

var barSensor = collector.CreateIntBarSensor("testSettings/testAlerts22222", sensorOptions);

await collector.Start();
```

or just set a description of a sensor

```C#
var collector = new DataCollector(productKey);

var barSensor = collector.Create10MinIntBarSensor("testSettings/testAlerts22222", "test description");

await collector.Start();
```

## Changing the description later
A sensor's description can be replaced after the sensor was created (HSMDataCollector 3.6.0), for example when it states a limit that can change while the application runs. `SetDescription` works on every sensor handle the collector returns (instant, last-value, rate, file, bar, function and service-commands sensors) and is safe to call from any thread.

```C#
using HSMDataCollector.Core;

var collector = new DataCollector(productKey);

var sensor = collector.CreateDoubleSensor("memory/used", "of the 1024 MB limit");

await collector.Start();

sensor.SetDescription("of the 2048 MB limit");
```

- Before `Start()` (and while the collector is stopped) the new text is what the next `Start()` registers.
- While the collector is running the sensor is re-registered on the server at once, with all its other settings unchanged.
- `null` leaves the description the server already has untouched; pass `""` to clear it.