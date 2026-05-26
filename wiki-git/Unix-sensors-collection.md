# Process
Sensors in this category collect the information about the current process (HSM Server process). C# Process class is used for sensors in the category.

### AddProcessCpu
The method creates sensor that collects CPU usage percentage into a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddProcessCpu(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddProcessCpu();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Unix.AddProcessCpu(options);

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
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddProcessMemory(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddProcessMemory();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Unix.AddProcessMemory(options);

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
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddProcessThreadCount();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Unix.AddProcessThreadCount(options);

dataCollector.Start();
```

### AddProcessMonitoringSensors
The current method calls the following methods:
* [AddProcessCpu](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Default-sensors-collection#addprocesscpu-1)
* [AddProcessMemory](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Default-sensors-collection#addprocessmemory-1)
* [AddProcessThreadCount](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Default-sensors-collection#addprocessthreadcount-1)

```C#
// Parameters:
//   options:
//     The custom options of the all process monitoring sensors. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Process monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

# System
Sensors in this category collect the information about the system. [Bash](https://en.wikipedia.org/wiki/Bash_(Unix_shell)) executing commands have been using

### AddTotalCpu
The method creates sensor that collects data about the whole CPU usage into ([top](https://www.geeksforgeeks.org/top-command-in-linux-with-examples/) command has been using) a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddTotalCpu(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddTotalCpu();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Unix.AddTotalCpu(options);

dataCollector.Start();
```

### AddFreeRamMemory
The method creates sensor that collects data about the amount of currently available RAM memory into ([free](https://www.geeksforgeeks.org/free-command-linux-examples/) command has been using) a bar and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddFreeRamMemory(BarSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddFreeRamMemory();

// or with custom options
// var options = new BarSensorOptions()
// {
//     NodePath = "Service monitoring",
//     BarPeriod = TimeSpan.FromMinutes(1),
//     CollectBarPeriod = TimeSpan.FromSeconds(10),
// };
// dataCollector.Unix.AddFreeRamMemory(options);

dataCollector.Start();
```

### AddSystemMonitoringSensors
The current method calls the following methods:
* [AddTotalCpu](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addtotalcpu)
* [AddFreeRamMemory](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addfreerammemory)

```C#
// Parameters:
//   options:
//     The custom options of the all system monitoring sensors. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     BarSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options = null);
```

# Disk
Sensors in this category collect the information about the disk. 'df' command is used for sensors in the category.

### AddFreeDiskSpace
The method creates sensor that gets current available free space of disk and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath is not used because there is always monitoring for root folder '/'.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min. 
IUnixCollection AddFreeDiskSpace(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddFreeDiskSpace();

// or with custom options
// var options = new DiskSensorOptions()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Unix.AddFreeDiskSpace(options);

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
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath is not used because there is always monitoring for root folder '/'.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
//         * CalibrationRequest - number of calibration requests. Default value is 6. 
IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddFreeDiskSpacePrediction();

// or with custom options
// var options = new DiskSensorOptions()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30),
//         CalibrationRequest = 4
// };
// dataCollector.Unix.AddFreeDiskSpacePrediction(options);

dataCollector.Start();
```

### AddDiskMonitoringSensors
The current method calls the following methods:
* [AddFreeDiskSpace](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Default-sensors-collection#addfreediskspace-1)
* [AddFreeDiskSpacePrediction](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Default-sensors-collection#addfreediskspaceprediction-1)

```C#
// Parameters:
//   options:
//     The custom options of the sensors to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     DiskSensorOptions contains the next parameters:
//         * TargetPath is not used because there is always monitoring for root folder '/'.
//         * NodePath - specific path to the sensor. Default value is "Disk monitoring".
//         * PostDataPeriod - POST method call time. Default value is 5 min.
//         * CalibrationRequest - number of calibration requests. Default value is 6. 
IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);
```

# Datacollector

### AddCollectorAlive
The method creates sensor that sends `true` boolean value to indicate that the monitored service is alive and has the next definition:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     CollectorMonitoringInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddCollectorAlive(SensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddCollectorAlive();

// or with custom options
// var options = new SensorOptions()
// {
//         NodePath = "Service monitoring",
//         PostDataPeriod = TimeSpan.FromSeconds(30)
// };
// dataCollector.Unix.AddCollectorAlive(options);

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
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     CollectorInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info/Collector".
IUnixCollection AddCollectorVersion(CollectorInfoOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddCollectorVersion();

// or with custom options
// var options = new CollectorInfoOptions ()
// {
//         NodePath = "Product Info/Collector",
// };
// dataCollector.Unix.AddCollectorVersion(options);

dataCollector.Start();
```

### AddCollectorStatus
The method creates sensor that sends current collector status ([More info](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/DataCollector-statuses)):
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     CollectorInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info/Collector".
IUnixCollection AddCollectorStatus(CollectorInfoOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddCollectorStatus();

// or with custom options
// var options = new CollectorInfoOptions ()
// {
//         NodePath = "Product Info/Collector",
// };
// dataCollector.Unix.AddCollectorStatus(options);

dataCollector.Start();
```
### AddCollectorMonitoringSensors
The current method calls the following methods:
* [AddCollectorAlive](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addcollectoralive)
* [AddCollectorVersion](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addcollectorversion)
* [AddCollectorStatus](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addcollectorstatus)
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     CollectorMonitoringInfoOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "System monitoring".
//         * PostDataPeriod - POST method call time. Default value is 15 sec.
//         * BarPeriod - period of a bar. Default value is 5 min.
//         * CollectBarPeriod - time between collection of sensor values. Default value is 5 sec.
IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptionsoptions = null);
```

# Other

### AddProductVersion
The method creates sensor that sends current connected product version after calling Start method:
```C#
// Parameters:
//   options:
//     The custom options of the sensor to create. If options is null the default options are using.
//
// Returns:
//     IUnixCollection instance for fluent interface.
//
// Remarks:
//     VersionSensorOptions contains the next parameters:
//         * NodePath - specific path to the sensor. Default value is "Product Info".
//         * Version - connected product version. Default value is "0.0.0".
//         * SensorName - specific sensor name. Default value is "Version".
//         * StartTime - specific time when the product has been started. Default value is DateTime.UtcNow.
IUnixCollection AddProductVersion(VersionSensorOptions options = null);
```

Example:
```C#
var dataCollector = new DataCollector(new CollectorOptions() { AccessKey = Key, ServerAddress = "https://localhost" });

dataCollector.Unix.AddProductVersion();

// or with custom options
// var options = new VersionSensorOptions()
// {
//         NodePath = "Product Info",
//         Version = Assembly.GetEntryAssembly()?.GetName().Version,
//         SensorName = "Version",
//         StartTime = DateTime.UtcNow,
// };
// dataCollector.Unix.AddProductVersion(options);

dataCollector.Start();
```