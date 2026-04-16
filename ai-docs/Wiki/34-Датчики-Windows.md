# Коллекция датчиков Windows

---

## Process

Датчики этой категории собирают информацию о текущем процессе (процессе HSM Server). Используется класс `PerformanceCounter`.

### AddProcessCpu

Создаёт датчик, собирающий процент использования ЦПУ.

```csharp
// Параметры: options (BarSensorOptions) — настраиваемые параметры. Если null, используются значения по умолчанию.
// Возвращает: IWindowsCollection для fluent-интерфейса.
// Примечание: BarSensorOptions содержит NodePath ("Process monitoring"), PostDataPeriod (15 сек), BarPeriod (5 мин), CollectBarPeriod (5 сек).
IWindowsCollection AddProcessCpu(BarSensorOptions options = null);
```

**Пример:**

```csharp
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });
dataCollector.Windows.AddProcessCpu();
dataCollector.Start();
```

### AddProcessMemory

Создаёт датчик, собирающий объём рабочего набора памяти текущего процесса.

```csharp
IWindowsCollection AddProcessMemory(BarSensorOptions options = null);
```

### AddProcessThreadCount

Создаёт датчик, собирающий количество потоков текущего процесса.

```csharp
IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);
```

### AddProcessMonitoringSensors

Вызывает `AddProcessCpu`, `AddProcessMemory` и `AddProcessThreadCount`.

```csharp
IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

---

## System

Датчики собирают информацию о системе. Используется `PerformanceCounter`.

### AddTotalCpu

Создаёт датчик общего использования ЦПУ.

```csharp
// Примечание: NodePath по умолчанию "System monitoring".
IWindowsCollection AddTotalCpu(BarSensorOptions options = null);
```

### AddFreeRamMemory

Создаёт датчик объёма доступной оперативной памяти.

```csharp
IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);
```

### AddSystemMonitoringSensors

Вызывает `AddTotalCpu` и `AddFreeRamMemory`.

```csharp
IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null);
```

---

## Disk

Датчики собирают информацию о дисках. Используется класс `DriveInfo`.

### AddFreeDiskSpace

Создаёт датчик свободного места на указанном диске.

```csharp
// Примечание: DiskSensorOptions содержит TargetPath (по умолчанию "C:\"), NodePath ("Disk monitoring"), PostDataPeriod (5 мин).
IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options = null);
```

### AddFreeDisksSpace

Создаёт датчики свободного места для всех дисков (`TargetPath` не используется).

```csharp
IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options = null);
```

### AddFreeDiskSpacePrediction

Создаёт датчик прогноза времени до заполнения диска.

```csharp
// Примечание: Добавлен CalibrationRequest (по умолчанию 6).
IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);
```

### AddFreeDisksSpacePrediction

Создаёт датчики прогноза для всех дисков.

```csharp
IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options = null);
```

### AddDiskMonitoringSensors

Вызывает `AddFreeDiskSpace` и `AddFreeDiskSpacePrediction`.

```csharp
IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);
```

---

## WindowsInfo

Датчики собирают информацию об ОС Windows. Используются `RegistryKey` и `PerformanceCounter`.

### AddWindowsNeedUpdate

Возвращает `true`, если система давно не обновлялась.

```csharp
// Примечание: WindowsSensorOptions содержит AcceptableUpdateInterval (по умолчанию 30 дней), NodePath ("Windows OS Info"), PostDataPeriod (12 часов).
IWindowsCollection AddWindowsNeedUpdate(WindowsSensorOptions options = null);
```

### AddWindowsLastUpdate

Создаёт датчик времени с последнего обновления системы.

```csharp
IWindowsCollection AddWindowsLastUpdate(WindowsSensorOptions options = null);
```

### AddWindowsLastRestart

Создаёт датчик времени с последней перезагрузки системы.

```csharp
IWindowsCollection AddWindowsLastRestart(WindowsSensorOptions options = null);
```

### AddWindowsInfoMonitoringSensors

Вызывает `AddWindowsNeedUpdate`, `AddWindowsLastUpdate` и `AddWindowsLastRestart`.

```csharp
IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsSensorOptions options = null);
```

---

## DataCollector

### AddCollectorAlive

Отправляет `true` для индикации работы отслеживаемого сервиса.

```csharp
// Примечание: SensorOptions содержит NodePath ("System monitoring"), PostDataPeriod (15 сек), BarPeriod (5 мин), CollectBarPeriod (5 сек).
IWindowsCollection AddCollectorAlive(SensorOptions options = null);
```

### AddCollectorVersion

Отправляет текущую версию DataCollector после вызова `Start`.

```csharp
// Примечание: CollectorInfoOptions содержит NodePath ("Product Info/Collector").
IWindowsCollection AddCollectorVersion(CollectorInfoOptions options = null);
```

### AddCollectorStatus

Отправляет текущий статус коллектора.

```csharp
IWindowsCollection AddCollectorStatus(CollectorInfoOptions options = null);
```

### AddCollectorMonitoringSensors

Вызывает `AddCollectorAlive`, `AddCollectorVersion` и `AddCollectorStatus`.

```csharp
IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);
```

---

## Other

### AddProductVersion

Отправляет версию подключённого продукта после вызова `Start`.

```csharp
// Примечание: VersionSensorOptions содержит NodePath ("Product Info"), Version ("0.0.0"), SensorName ("Version"), StartTime (DateTime.UtcNow).
IWindowsCollection AddProductVersion(VersionSensorOptions options = null);
```

## Связанные документы

- [[32-DataCollector-Обзор]] — обзор DataCollector
- [[16-Типы-датчиков]] — типы датчиков
- [[35-Датчики-Unix]] — аналогичные датчики для Unix-систем
- [[28-DataCollector-Настройки-датчиков]] — настройки датчиков
