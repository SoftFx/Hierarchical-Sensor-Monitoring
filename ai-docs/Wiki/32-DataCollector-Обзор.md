# DataCollector — Обзор и API

HSMDataCollector — это NuGet-пакет для .NET-приложений, который собирает системные метрики (ЦП, память, диск) и отправляет пользовательские значения датчиков на сервер HSM.

---

## Основные возможности

* Отправка данных датчиков на сервер HSM по HTTPS
* Встроенные системные датчики (ЦП, память, диск, процессы)
* Создание пользовательских датчиков
* Поддержка мгновенных и bar-датчиков
* Настройка оповещений для датчиков
* Интеграция с Grafana
* Логирование (NLog или пользовательский логгер)

---

## Быстрый старт

```csharp
using HSMDataCollector.Core;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://your-hsm-server",
    AccessKey = "YOUR_ACCESS_KEY"
});

collector.Windows.AddSystemMonitoringSensors()
                 .AddProcessMonitoringSensors()
                 .AddCollectorMonitoringSensors();

await collector.Start();

var cpuSensor = collector.CreateDoubleSensor("MyApp/cpu_usage");
cpuSensor.AddValue(42.5);

await collector.Stop();
```

---

## Состояния DataCollector

| Статус | Описание |
| :--- | :--- |
| **Starting** | Запуск: синхронизация с сервером, создание сессии, запуск датчиков |
| **Running** | Работа: отправка данных на сервер |
| **Stopping** | Остановка: отправка оставшихся значений, очистка очереди, закрытие сессии |
| **Stopped** | Остановлен |

---

## Логирование

```csharp
// С NLog по умолчанию
var collector = new DataCollector(collectorOptions).AddNLog();

// С пользовательскими настройками NLog
var collector = new DataCollector(collectorOptions).AddNLog(loggerOptions);

// С пользовательским логгером
var collector = new DataCollector(collectorOptions).AddCustomLogger(new CustomLogger());
```

---

## Настройки датчиков

### Мгновенные датчики

```csharp
var sensorOptions = new InstantSensorOptions()
{
    Description = "описание",
    SensorUnit = Unit.KB,
    KeepHistory = TimeSpan.FromDays(31),
    SelfDestroy = TimeSpan.FromDays(31),
    EnableForGrafana = true,
    TtlAlert = AlertFactory.IfInactivityPeriodIs(TimeSpan.FromMinutes(15))
        .ThenSetIcon("🎃")
        .AndNotify("Time is over")
        .Build(),
    Alerts = new List<InstantAlertTemplate> { ... }
};
```

### Bar-датчики

## Связанные документы

- [[05-Быстрый-старт]] — пример запуска DataCollector
- [[26-DataCollector-Состояния]] — состояния коллектора
- [[27-DataCollector-Логирование]] — настройка логирования
- [[28-DataCollector-Настройки-датчиков]] — настройки датчиков

```csharp
var sensorOptions = new BarSensorOptions()
{
    Description = "описание",
    SensorUnit = Unit.MB,
    PostDataPeriod = TimeSpan.FromSeconds(15),
    BarTickPeriod = TimeSpan.FromSeconds(5),
    BarPeriod = TimeSpan.FromMinutes(5),
    KeepHistory = TimeSpan.FromDays(31),
    EnableForGrafana = true,
    TtlAlert = ...,
    Alerts = new List<BarAlertTemplate> { ... }
};
```
