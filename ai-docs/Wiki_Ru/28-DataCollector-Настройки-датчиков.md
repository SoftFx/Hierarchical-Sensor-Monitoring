# Настройки датчиков DataCollector

---

## Мгновенный датчик

Полный пример настройки мгновенного датчика:

```csharp
var collector = new DataCollector(productKey);

var sensorOptions = new InstantSensorOptions()
{
    Description = "тесты",
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

Или просто задать описание датчика:

```csharp
var collector = new DataCollector(productKey);

var sensor = collector.CreateDoubleSensor("testSettings/testAlerts22222", "test description");

await collector.Start();
```

---

## Bar-датчик

Полный пример настройки bar-датчика:

```csharp
var collector = new DataCollector(productKey);

var sensorOptions = new BarSensorOptions()
{
    Description = "тесты",
    SensorUnit = Unit.MB,

    PostDataPeriod = TimeSpan.FromSeconds(15), // как часто отправлять текущий бар
    BarTickPeriod = TimeSpan.FromSeconds(5), // как часто обновлять значение текущего бара
    BarPeriod = TimeSpan.FromMinutes(5), // временной промежуток текущего бара

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

## Связанные документы

- [[32-DataCollector-Обзор]] — общий обзор DataCollector
- [[16-Типы-датчиков]] — описание типов датчиков
- [[10-Оповещения-Обзор]] — настройка оповещений для датчиков
- [[05-Быстрый-старт]] — пример создания датчиков

Или просто задать описание датчика:

```csharp
var collector = new DataCollector(productKey);

var barSensor = collector.Create10MinIntBarSensor("testSettings/testAlerts22222", "test description");

await collector.Start();
```
