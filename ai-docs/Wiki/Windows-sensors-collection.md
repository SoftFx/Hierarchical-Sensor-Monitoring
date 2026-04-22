# Windows Sensors Collection

This page describes the built-in Windows sensors available in the HSM DataCollector.

See also: [HSM DataCollector](HSMDataCollector), [Unix Sensors Collection](Unix-sensors-collection), [Sensor Types](Sensor-types)

---

## Process

Sensors in this category collect the information about the current process (HSM Server process). PerformanceCounter class is used for sensors in the category.

### AddProcessCpu
The method creates sensor that collects CPU usage percentage into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddProcessCpu(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddProcessCpu();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddProcessCpu(options);

dataCollector.Start();
```

### AddProcessMemory
The method creates sensor that collects current process working set into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddProcessMemory(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddProcessMemory();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddProcessMemory(options);

dataCollector.Start();
```

### AddProcessThreadCount
The method creates sensor that gets the amount of threads, associated with current process, and collects it into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddProcessThreadCount();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddProcessThreadCount(options);

dataCollector.Start();
```

### AddProcessMonitoringSensors
The current method calls the following methods:
* [AddProcessCpu](#addprocesscpu)
* [AddProcessMemory](#addprocessmemory)
* [AddProcessThreadCount](#addprocessthreadcount)

```C#
// Parameters:
//   options:
//     The custom options of the all process monitoring sensors. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

# System
Sensors in this category collect the information about the system. PerformanceCounter class is used for sensors in the category.

### AddTotalCpu
The method creates sensor that collects data about the whole CPU usage into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddTotalCpu(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddTotalCpu();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddTotalCpu(options);

dataCollector.Start();
```

### AddFreeRamMemory
The method creates sensor that collects data about the amount of currently available RAM memory into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddFreeRamMemory();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddFreeRamMemory(options);

dataCollector.Start();
```

### AddSystemMonitoringSensors
The current method calls the following methods:
* [AddTotalCpu](#addtotalcpu)
* [AddFreeRamMemory](#addfreerammemory)

```C#
// Parameters:
//   options:
//     The custom options of the all system monitoring sensors. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

## Disk
Sensors in this category collect the information about the disk(s). DriveInfo class is used for sensors in the category.

### AddFreeDiskSpace
The method creates sensor that gets current available free space of some disk and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath - A valid drive path or drive letter. This can be either uppercase or lowercase, 'a' to 'z'. Default value is "C:\". A null value is not valid.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min. 
IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddFreeDiskSpace();

// or with custom options
// var options = new DiskSensorOptions()
// {
//         TargetPath = "D:\",
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Windows.AddFreeDiskSpace(options);

dataCollector.Start();
```

### AddFreeDisksSpace
The method creates sensors that get current available free space of all disks and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the disk monitoring sensor for all drives. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath is not used because there is monitoring of all disks.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddFreeDisksSpace();

// or with custom options
// var options = new DiskSensorOptions()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Windows.AddFreeDisksSpace(options);

dataCollector.Start();
```

### AddFreeDiskSpacePrediction
The method creates sensor that gets estimated time until disk space runs out and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath - A valid drive path or drive letter. This can be either uppercase or lowercase, 'a' to 'z'. Default value is "C:\". A null value is not valid.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
//         * CalibrationRequest - number of calibration requests. Default value is 6. 
IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddFreeDiskSpacePrediction();

// or with custom options
// var options = new DiskSensorOptions()
// {
//	   TargetPath = "D:\"
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30),
//         CalibrationRequest = 4
// };
// dataCollector.Windows.AddFreeDiskSpacePrediction(options);

dataCollector.Start();
```
### AddFreeDisksSpacePrediction
The method creates sensorы that get estimated time until disks space runs out and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the disk monitoring sensor for all drives. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath is not used because there is monitoring of all disks.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
//         * CalibrationRequest - number of calibration requests. Default value is 6. 
IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddFreeDisksSpacePrediction();

// or with custom options
// var options = new AddFreeDisksSpacePrediction()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30),
//         CalibrationRequest = 4
// };
// dataCollector.Windows.AddFreeDisksSpacePrediction(options);

dataCollector.Start();
```

### AddDiskMonitoringSensors
The current method calls the following methods:
* [AddFreeDiskSpace](#addfreediskspace)
* [AddFreeDiskSpacePrediction](#addfreediskspaceprediction)

```C#
// Parameters:
//   options:
//     The custom options of the sensors to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath - A valid drive path or drive letter. This can be either uppercase or lowercase, 'a' to 'z'. Default value is "C:\". A null value is not valid.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
//         * CalibrationRequest - number of calibration requests. Default value is 6. 
IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);
```

## Windows Info
Sensors in this category collect the information about the Windows OS. RegistryKey and PerformanceCounter classes are used for sensors in the category.

### AddWindowsNeedUpdate
The method creates sensor that gets `true` if the system has not been updated for a long time and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     WindowsSensorOptions contains the next parameters:
//         * AcceptableUpdateInterval - minimum acceptable time value after windows update. Default value is 30 days.
//         * NodePath - specific path to the sensor. Default value is "Windows OS Info".
//         * PostDataPeriod - POST method call time. Default value is 12 hours.
IWindowsCollection AddWindowsNeedUpdate(WindowsSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddWindowsNeedUpdate();

// or with custom options
// var options = new WindowsSensorOptions()
// {
//         NodePath = "OS monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30),
//         AcceptableUpdateInterval = TimeSpan.FromDays(15)
// };
// dataCollector.Windows.AddWindowsNeedUpdate(options);

dataCollector.Start();
```

### AddWindowsLastUpdate
The method creates sensor that gets time since last system update and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     WindowsSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Windows OS Info".
//         * PostDataPeriod - POST method call time. Default value is 12 hours.
IWindowsCollection AddWindowsLastUpdate(WindowsSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddWindowsLastUpdate();

// or with custom options
// var options = new WindowsSensorOptions()
// {
//         NodePath = "OS monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Windows.AddWindowsLastUpdate(options);

dataCollector.Start();
```

### AddWindowsLastRestart
The method creates sensor that gets time since last system restart and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     WindowsSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Windows OS Info".
//         * PostDataPeriod - POST method call time. Default value is 12 hours.
IWindowsCollection AddWindowsLastRestart(WindowsSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddWindowsLastRestart();

// or with custom options
// var options = new WindowsSensorOptions()
// {
//         NodePath = "OS monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Windows.AddWindowsLastRestart(options);

dataCollector.Start();
```

### AddWindowsInfoMonitoringSensors
The current method calls the following methods:
* [AddWindowsNeedUpdate](#addwindowsneedupdate)
* [AddWindowsLastUpdate](#addwindowslastupdate)
* [AddWindowsLastRestart](#addwindowslastrestart)

```C#
// Parameters:
//   options:
//     The custom options of the all Windows monitoring sensors. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     WindowsSensorOptions contains the next parameters:
//         * AcceptableUpdateInterval - minimum acceptable time value after windows update. Default value is 30 days.
//         * NodePath - specific path to the sensor. Default value is "Windows OS Info".
//         * PostDataPeriod - POST method call time. Default value is 12 hours.
IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsSensorOptions options = null);
```

## DataCollector

### AddCollectorAlive
The method creates sensor that sends `true` boolean value to indicate that the monitored service is alive and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     SensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddCollectorAlive(SensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddCollectorAlive();

// or with custom options
// var options = new SensorOptions()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30),
//         BarPeriod = TimeSpan.FromMinutes(1),
//         CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Windows.AddCollectorHeartbeat(options);

dataCollector.Start();
```

### AddCollectorVersion
The method creates sensor that sends current DataCollector version after calling Start method:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     CollectorInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info/Collector".
IWindowsCollection AddCollectorVersion(CollectorInfoOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddCollectorVersion();

// or with custom options
// var options = new CollectorInfoOptions ()
// {
//         NodePath = "Product Info/Collector",
// };
// dataCollector.Windows.AddCollectorVersion(options);

dataCollector.Start();
```

### AddCollectorStatus
The method creates sensor that sends current collector status ([More info](DataCollector-statuses)):
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     CollectorInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info/Collector".
IWindowsCollection AddCollectorStatus(CollectorInfoOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddCollectorStatus();

// or with custom options
// var options = new CollectorInfoOptions ()
// {
//         NodePath = "Product Info/Collector",
// };
// dataCollector.Windows.AddCollectorStatus(options);

dataCollector.Start();
```
### AddCollectorMonitoringSensors
The current method calls the following methods:
* [AddCollectorAlive](#addcollectoralive)
* [AddCollectorVersion](#addcollectorversion)
* [AddCollectorStatus](#addcollectorstatus)
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     CollectorMonitoringInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions = null);
```

## Other

### AddProductVersion
The method creates sensor that sends current connected product version after calling Start method:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IWindowsCollection instance for fluent interface.
//
// Remarks:
//     VersionSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info".
//         * Version - connected product version. Default value is "0.0.0".
//         * SensorName - specific sensor name. Default value is "Version".
//         * StartTime - specific time when the product has been started. Default value is DateTime.UtcNow.
IWindowsCollection AddProductVersion(VersionSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Windows.AddProductVersion();

// or with custom options
// var options = new VersionSensorOptions()
// {
//         NodePath = "Product Info",
//         Version = Assembly.GetEntryAssembly()?.GetName().Version,
//         SensorName = "Version",
//         StartTime = DateTime.UtcNow,
// };
// dataCollector.Windows.AddProductVersion(options);

dataCollector.Start();
```

---

## See Also

- [HSM DataCollector](HSMDataCollector) — Full DataCollector API reference
- [Unix Sensors Collection](Unix-sensors-collection) — Built-in sensors for Linux/Unix systems
- [Sensor Types](Sensor-types) — Available sensor types and their properties
- [DataCollector Statuses](DataCollector-statuses) — Collector lifecycle states