# HSM Server

## New entity Dashboard has been added
New entity for HSM server. Every Dashboard includes a several Multichart panels that update in real time. Dashboard consists of:
1. Name
1. Description *(Markdown is supported)*
1. TimePeriod (for what period data on charts displays)
1. List of Multichart panels

Also some key points about Dashboards:
* New tab **Dashboards** has been added
* Dashbord can include data from different products and folders
* Dashboard saves Multichart panels positions
* You can **resize** all Multichart panels as you like
* Multichart panels are uploaded every **30 seconds**
* Dashboards are saved in a special database called **Server layout**
* Every Dashboard has **Autofitting** logic and can sort all your charts 2 or 3 per column automatically
* You can **View/Hide legend** for every Multichart panel

## New entity Multichart has been added 
This is a special chart that supports a several sources at once. Can be created only as a part of Dashboard. Supports only line graphics (like Integer, Double and TimeSpan sensors). Also current chart supports aggregation data logic with a special tooltip. Every Multichart consists of:
1. Name
1. Description *(Markdown is supported)*
1. DashboardId
1. List of data sourses

Also some key points about Multichart panels:
* Max number of visible data is 100 points per datasource. If current count of data is greater than 100, all points are aggregated.
* Datasources support **Drap and Drop** logic from Tree

## New entity Datasource has been added
This is a part of Multichart entity, which describes how to plot different sensor data. That entity consists of:
1. Label - how to label data on a chart
1. Color
1. SensorId - from which sensor data for line rendering should be taken

## Database
* **Shapshot database** has been renamed to **Snapshot database**.
* **Server layout** database has been added in backup logic.

## Sensor info
* **Alerts** have been added to View mode by default.
* **TTL FromParent (Never)** has been restored as visible alert.
* **EMA statistics** toggle switch has been added.

## Sensor settings
* EMA calculation for sensors have been added. **EMA (Value)** for Integer and Double sensors and **EMA (Min)**, **EMA (Mean)**, **EMA (Max)** and **EMA (Count)** for DoubleBar and IntegerBar sensors.
* New units **Requests**, **Responses** and **Count** have been added.

## Alerts
* View for alerts with empty notifications has been improved.
* New **EMA (Value)** property for instant sensors has been added.
* New **EMA (Min)**, **EMA (Mean)**, **EMA (Max)** and **EMA (Count)** properties for DoubleBar and IntegerBar sensors have been added.
* New **FirstValue** property for bar sensors have been added.
* Template variables for new properties have been added.

## Table history
* **Last value** and **Count** columns have been swapped.
* **First value** column for bar sensors has been added.
* **First value** and **Last value** columns are hidden by default.
* **EMA** columns have been added. They are visible only if **EMA statistics** setting is switched on.

## Other
* Names of nodes have been added to confirmation dialog for **Multi removing** logic.
* View for **TimeSpan zero** value has been fixed (now it is 0 seconds).
* All items **Delete** have been renamed to **Remove**.
* Searching by **Path** column has been added in Journal.

# HSM Datacollector v.3.2.4

## New default group **Diagnostic sensors** has been added
* **Package content size** sensor has been added in *.module/Collector queue stats*. This sensor calculates a body size for each package in Collector messages queue.
* **Package process time** sensor has been added in *.module/Collector queue stats*. This sensor calculates an average process time for each value in package in Collector messages queue.
* **Values count in package** sensor has been added in *.module/Collector queue stats*. This sensor calculates a total count of values in each package in Collector messages queue.
* **Queue overflow** sensor has been added in *.module/Collector queue stats*. This sensor calculates a total count of values which have been removed from Collector messages queue without sending to Server.

## Default sensors
* New facade for all sensors **AddAllDefaultSensors** has been added.
* New facade for all module sensors **AddAllModuleSensors** has been added.
* New facade for all computer sensors **AddAllComputerSensors** has been added.
* **Collector errors** sensor has been added in *.module*. Sends all collector errors and exceptions to the server.
* New default sensors **Windows OS info/Version & patch** have been added.
* **Time in GC** - new computer default sensor has been added (total computer time).
* New process default sensor **Process time in GC** has been added.

## Sensors changes
* Default prediction logic for **Free disk space prediction** has been changed. Now it uses speed EMA. Default alerts have been removed.
* Preformance counter category for all disks sensors have been changed from **Physical Disk** to **Logical disk**.
* All bar alerts have been changed from **Mean** to **EMA (Mean)** with 5 min **Confirmation period**.

## Sensors settings
* New setting **IsPrioritySensor** has been added. If **IsPrioritySensor** is true, then data skip global message queue and send directly to a HSM server.
* New setting **Statistics** has been added. You can swith on server side EMA calculations.

## Alert API

## Bugfixing
* **Windows Info/Last Update** and **Windows Info/Last restart** sensors have been fixed.