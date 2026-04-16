# Логирование DataCollector

---

## По умолчанию

NLog используется по умолчанию. Настройки по умолчанию:

* WriteDebug = false
* ConfigPath = "collector.nlog.config"

```csharp
var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", // должен быть изменён
    ServerAddress = "https://localhost",
};

var collector = new DataCollector(collectorOptions).AddNLog();
```

---

## Пользовательские настройки

Вы можете изменить настройки NLog по умолчанию следующим образом:

```csharp
var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", // должен быть изменён
    ServerAddress = "https://localhost",
};
var loggerOptions = new LoggerOptions()
{
    ConfigPath = "logger.config",
    WriteDebug = true,
};

var collector = new DataCollector(collectorOptions).AddNLog(loggerOptions);
```

---

## Пользовательский логгер

Вы можете использовать свой собственный логгер. Он должен реализовывать интерфейс `ICollectorLogger`.

```csharp
internal sealed class CustomLogger : ICollectorLogger
{
    public void Debug(string message) => Console.WriteLine($"Debug: {message}");

    public void Info(string message) => Console.WriteLine($"Info: {message}");

    public void Error(string message) => Console.WriteLine($"Error: {message}");

    public void Error(Exception ex) => Console.WriteLine($"Exception: {ex.Message}");
}
```

```csharp
var collectorOptions = new CollectorOptions()
{
    AccessKey = "e6150991-08a8-48dc-8152-0458715a1e3c", // должен быть изменён
    ServerAddress = "https://localhost",
};

var collector = new DataCollector(collectorOptions).AddCustomLogger(new CustomLogger());
```

## Связанные документы

- [[32-DataCollector-Обзор]] — общий обзор DataCollector
- [[26-DataCollector-Состояния]] — состояния коллектора
- [[05-Быстрый-старт]] — пример добавления логирования
