# HSM Server

# HSM Datacollector

### Structure and optimizations
* Async requests and handlers for HttpClient have been added
* Base structure for **Simple sensor** (a sensor that sends data on user request, not on a timer) has been added
* Collector statuses have been added. Now collector has 4 statuses: Starting, Running, Stopping, Stopped

### Default sensors
* **CollectorAlive** sensor has been renamed to **CollectorHeartbeat**. Sensor name has been renamed from **Service alive** to **Service heartbeat**
* New default sensor **Product info** has been added. Now it contains Product Version with Version start time
* New defaut sensor **Collector status** has been added. It describes current collector state and contains error message (if exsists)

### Unix sensors
* **Free RAM memory MB** has been added
* **Total CPU** has been added
* **AddSystemMonitoringSensors** facade for **Free RAM memory MB** and **Total CPU** has been added

### New methods
* New method **SendFileAsync** has been added

### Other
* Collector version has been increased to 3.1.0