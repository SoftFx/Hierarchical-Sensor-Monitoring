![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch1.png)

Main modules:

* **HSM Server** - the main service of the project, is the end point for user data. Serves for processing, validating and storing data;
* **HSM objects** - api of the product, contains a description in what form the values ​​should come to the HSM Server for correct processing;
* **Datacollector** - nugget with classes for correct requests to the server and various sensor implementations for the computer;
* **Wrapper** - a Datacollector wrapper written in C++ to be used in positive projects.

![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch4.png)

**Server:**

* HSMServer - web server for receiving data from clients and managing client sites
* HSMServer.Core - library for storing and recalculating the global state of the sensor tree
* HSMServer.Core.Monitoring - library for monitoring HSM server

**Database:**

* HSMDatabase - database manager, serves as a facade for querying the database
* HSMDatabase.AccessManager - database entity format protocol, needed to isolate the database implementation
* HSMDatabase.LevelDB - binary database NoSQL format, isolated from the general assembly and works through the AccessManager

**LevelDB:**

* EnviromentData - a database with meta-information about the xcm structure (users, products, sensors, keys, filters, settings, etc.)
* SensorValues ​​- lightweight sensor database (introduced after Sprint 8)
* MonitoringData - old format database (write disabled after Sprint 8, read disabled after Sprint 12)

![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch5.png)