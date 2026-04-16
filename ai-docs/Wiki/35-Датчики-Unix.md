# Коллекция датчиков Unix

---

## Process

Датчики в этой категории собирают информацию о текущем процессе (процессе HSM Server). Используется C# класс `Process`.

### AddProcessCpu

Метод создаёт датчик, собирающий процент использования ЦПУ в бар.

```csharp
// Параметры:
// options: пользовательские настройки создаваемого датчика. Если null, используются настройки по умолчанию.
//
// Возвращает: экземпляр IUnixCollection для fluent-интерфейса.
//
// Примечание:
// BarSensorOptions содержит следующие параметры:
// * NodePath — определённый путь к датчику. Значение по умолчанию: "Process monitoring".
// * PostDataPeriod — время вызова POST-метода. Значение по умолчанию: 15 сек.
// * BarPeriod — период бара. Значение по умолчанию: 5 мин.
// * CollectBarPeriod — время между сборами значений датчика. Значение по умолчанию: 5 сек.
IUnixCollection AddProcessCpu(BarSensorOptions options = null);
```

**Пример:**

```csharp
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });
dataCollector.Unix.AddProcessCpu();
dataCollector.Start();
```

### AddProcessMemory

Метод создаёт датчик, собирающий рабочий набор текущего процесса в бар.

```csharp
IUnixCollection AddProcessMemory(BarSensorOptions options = null);
```

### AddProcessThreadCount

Метод создаёт датчик, получающий количество потоков, связанных с текущим процессом, и собирающий их в бар.

```csharp
IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);
```

### AddProcessMonitoringSensors

Этот метод вызывает следующие методы:

* AddProcessCpu
* AddProcessMemory
* AddProcessThreadCount

```csharp
IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

---

## System

Датчики в этой категории собирают информацию о системе. Используются команды, выполняемые через bash.

### AddTotalCpu

Метод создаёт датчик, собирающий данные об общем использовании ЦПУ в бар (используется команда `top`).

```csharp
// BarSensorOptions: NodePath = "System monitoring", PostDataPeriod = 15 сек, BarPeriod = 5 мин, CollectBarPeriod = 5 сек.
IUnixCollection AddTotalCpu(BarSensorOptions options = null);
```

### AddFreeRamMemory

Метод создаёт датчик, собирающий данные об объёме доступной оперативной памяти в бар (используется команда `free`).

```csharp
IUnixCollection AddFreeRamMemory(BarSensorOptions options = null);
```

### AddSystemMonitoringSensors

Этот метод вызывает следующие методы:

* AddTotalCpu
* AddFreeRamMemory

```csharp
IUnixCollection AddSystemMonitoringSensors(BarSensorOptions options = null);
```

---

## Disk

Датчики в этой категории собирают информацию о диске. Для датчиков используется команда `df`.

### AddFreeDiskSpace

Метод создаёт датчик, получающий текущее доступное свободное место на диске.

```csharp
// DiskSensorOptions: TargetPath не используется (всегда мониторся корневая папка '/'), NodePath = "Disk monitoring", PostDataPeriod = 5 мин.
IUnixCollection AddFreeDiskSpace(DiskSensorOptions options = null);
```

### AddFreeDiskSpacePrediction

Метод создаёт датчик, оценивающий время до полного заполнения диска.

```csharp
// DiskSensorOptions: CalibrationRequest — количество запросов калибровки. Значение по умолчанию: 6.
IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);
```

### AddDiskMonitoringSensors

Этот метод вызывает следующие методы:

* AddFreeDiskSpace
* AddFreeDiskSpacePrediction

```csharp
IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);
```

---

## DataCollector

### AddCollectorAlive

Метод создаёт датчик, отправляющий булево значение `true` для индикации того, что отслеживаемый сервис работает.

```csharp
// SensorOptions: NodePath = "System monitoring", PostDataPeriod = 15 сек, BarPeriod = 5 мин, CollectBarPeriod = 5 сек.
IUnixCollection AddCollectorAlive(SensorOptions options = null);
```

### AddCollectorVersion

Метод создаёт датчик, отправляющий текущую версию DataCollector после вызова метода Start.

```csharp
// CollectorInfoOptions: NodePath = "Product Info/Collector".
IUnixCollection AddCollectorVersion(CollectorInfoOptions options = null);
```

### AddCollectorStatus

Метод создаёт датчик, отправляющий текущий статус коллектора.

```csharp
IUnixCollection AddCollectorStatus(CollectorInfoOptions options = null);
```

### AddCollectorMonitoringSensors

Этот метод вызывает следующие методы:

* AddCollectorAlive
* AddCollectorVersion
* AddCollectorStatus

```csharp
IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);
```

---

## Other

### AddProductVersion

Метод создаёт датчик, отправляющий версию подключённого продукта после вызова метода Start.

```csharp
// VersionSensorOptions: NodePath = "Product Info", Version = "0.0.0", SensorName = "Version", StartTime = DateTime.UtcNow.
IUnixCollection AddProductVersion(VersionSensorOptions options = null);
```

## Связанные документы

- [[32-DataCollector-Обзор]] — обзор DataCollector
- [[16-Типы-датчиков]] — типы датчиков
- [[34-Датчики-Windows]] — аналогичные датчики для Windows-систем
- [[28-DataCollector-Настройки-датчиков]] — настройки датчиков
