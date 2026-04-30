# DataCollector Sensor Settings

This page provides full configuration examples for instant sensors and bar sensors in the HSM DataCollector.

See also: [HSM DataCollector](HSMDataCollector), [DataCollector Logging](DataCollector-logging), [Sensor Types](Sensor-types)

---

## Instant Sensor

Full setup example for an instant sensor:

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

---

## See Also

- [HSM DataCollector](HSMDataCollector) — Full DataCollector API reference
- [Sensor Types](Sensor-types) — Available sensor types and their properties
- [Alert Templates](Alert-Templates) — How to configure alert templates for sensors
- [Alert Conditions Reference](Alert-Conditions-Reference) — Available conditions for each sensor type

or just set a description of a sensor

```C#
var collector = new DataCollector(productKey);

var barSensor = collector.Create10MinIntBarSensor("testSettings/testAlerts22222", "test description");

await collector.Start();
```