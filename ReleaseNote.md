# HSM Server

## New sensor type [**Version**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Sensor-types) has been added:
* New endpoint **/version** has been added
* New table for sensor type **Version** has been added. Version format *1.2.3.4*

## New type of Access keys has been added - **Master key**
* Master key has access to ALL products on the server
* **Admin ONLY** can create Master key

## Ability to integrate with [**Grafana**](https://grafana.com/) has been added:
* New endopoints for [**JsonDatasource**](https://grafana.com/grafana/plugins/simpod-json-datasource/) have been added: */grafana/JsonDatasource*, */grafana/JsonDatasource/metrics*, */grafana/JsonDatasource/metric-payload-options*, */grafana/JsonDatasource/query*
* Grafana connection guide is [here](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Integration-with-Grafana)
* List of available datasources and sensor types is [here](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Supported-Grafana-datasources#json-datasource)

## Site
* New sensor status **Empty sensor** has been added with white circle (if sensor history is empty)
* Unused settings have been hidden in Configuration tab
* Validation on min value for interval control (TTL, Sensativity) has been added

## Tree
* **Enable/Disable Grafana** item has been added in Context menu
* Grafana icon is shown for sensors with Grafana enabled setting

## Tree filters
* **History** has been renamed to **Visibility**
* **No data** has been renamed to **Empty sensors**
* **Show icons** setting has been added
* **Integrations** group with **Grafana** property has been added

## Sensor info
* New property **Enable for** has been added
* Auto update by Update tree interval has been added
* Manually change for sensor status has been added (available for **ALL** users)

## Access keys
* Select product input has been added in Edit modal
* New link for creating access keys has been added on Access Keys tab (Only for admins)
* Unique access key name validation has been added in Edit modal
* **Unselect all** button has been added in Edit modal
* Key's authors that have been removed have been marked as *Removed*

## Bugfixing
* Redirect to Home page after filters applying has been fixed

# HSM Datacollector

### Structure and optimizations
* Async requests and handlers for HttpClient have been added
* Base structure for **Simple sensor** (a sensor that sends data on user request, not on a timer) has been added
* Collector statuses have been added. Now collector has 4 statuses: **Starting**, **Running**, **Stopping**, **Stopped**. [More info](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/DataCollector-statuses)

### Default sensors
* New default sensor [**Product info**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Windows-sensors-collection#addproductversion) has been added. Now it contains Product Version with Version start/stop time.
* New defaut sensor [**Collector status**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addcollectorstatus) has been added. It describes current collector state and contains error message (if exsists).

### Unix sensors
* [**Free RAM memory MB**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addfreerammemory) has been added.
* [**Total CPU**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addtotalcpu) has been added.
* [**AddSystemMonitoringSensors**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Unix-sensors-collection#addsystemmonitoringsensors) facade for **Free RAM memory MB** and **Total CPU** has been added.

### New methods
* New method **SendFileAsync** has been added

### Other
* Collector version has been increased to 3.1.5
* Package sending has been reworked. Packages will be sent until the queue is empty
* Default queue size has been increased to 20.000 items
* Default PostTime for WindowsInfo and DiskMonitroing sensors has been increased to 6h and 5min
* The collector version will be logged

# HSM DataObjects

* New sensor type **Version** has been added
* DataObjects version has been increased to 3.0.2