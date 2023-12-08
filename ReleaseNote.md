# HSM Server

## New entity Dashboards have been added
New entity for HSM server. Every Dashboard includes a several Multicharts that update in real time. Dashboard consists of:
1. Name
1. Description *(Markdown is supported)*
1. TimePeriod (for what period to display data on charts)
1. List of Multicharts

Also some key points of Dashboards:
* New tab **Dashboards** has been added
* Dashbord can include data with different products and folders
* Only server Admin and Dashboard creator can change some settings in a board
* Dashboard saves Multicharts positions
* You can **resize** all Multicharts as you like
* Multicharts are uploaded every **30 seconds**
* Dashboards saved in a special database called **Server layout**
* Every Dashboard has **Autofitting** logic and can automatically sort all your charts 2 or 3 per column
* You can **View/Hide legend** for every Multichart

## New entity Multicharts have been added 
This is a special chart that supports a several sources at once. Can be created only as a part of Dashboard. Supports only line graphics (like Integer, Double and TimeSpan sensors). Also current chart support logic of aggregation data with a special tooltip. Every Multichart consists of:
1. Name
1. Description *(Markdown is supported)*
1. DashboardId
1. List of Data sourses

Also some key points of Multicharts:
* Max number of visible data is 100 points per datasource. If current count of data is greater than 100, all points are aggregated.
* Datasources support **Drap and Drop** logic from Tree

## New entity Datasource
This is a part of Multichart entity, witch describes how to plot different sensor data. That entity consists of:
1. Label - how to label data on a chart
1. Color
1. SensorId - from witch sensor take data for line rendering

## Database
* **Shaphot database** has been removed to **Snapshot database**.
* **Server layout** database has been added in backup logic.

## Sensor info
* **Alerts** have been added to View mode by default.
* **TTL FromParent(Never)** has been restored as visible alert.
* **Calculate EMA** switcher has been added.

## Sensor settings
* EMA calculation for sensors have been added. **EMAValue** for Integer and Double sensors and **EMAMin**, **EMAMean**, **EMAMax** and **EMACount** for DoubleBar and IntegerBar sensors.
* New units **Requests**, **Responses** and **Count** have been added.

## Alerts
* View for alerts with empty notifications has been improved.
* New **EMAValue** property for instant sensors has been added.
* New **EMAMin**, **EMAMean**, **EMAMax** and **EMACount** properties for DoubleBar and IntegerBar sensors have been added.
* New **FirstValue** property for bar sensors have been added.

## Table history
* **Last value** and **Count** columns have been swapped.
* **First value** column for bar sensors has been added.
* **First value** and **Last value** columns are hidden by default.
* **EMA** columns have been added. They visible only if **Calculate EMA** setting is on.

## Other
* Names of removed nodes have been added to dialog window for **Multi removing** logic.
* View for **TimeSpan zero** value has been fixed (now it is 0 seconds).
* All items **Delete** have been renamed to **Remove**.
* Searching by **Path** has been added in Journal.

# HSM Datacollector v.3.2.4

## New default group **Diagnostic sensors** has been added
* **Package content size** sensor has been added in *.module/Collector queue stats*. This sensor calculate a body size for each package for Collector message queue.
* **Package process time** sensor has been added in *.module/Collector queue stats*. This sensor calculate a average process time for each value in package for Collector message queue.
* **Values count in package** sensor has been added in *.module/Collector queue stats*. This sensor calculate a total count of values in each package for Collector message queue.
* **Queue overflow** sensor has been added in *.module/Collector queue stats*. This sensor calculate a total count of values witch have been removed from Collector message queue without sending to Server.

## New default sensors
* New facade for all sensors **AddAllDefaultSensors** has been added.
* New facade for all module sensors **AddAllModuleSensors** has been added.
* New facade for all computer sensors **AddAllComputerSensors** has been added.
* **Collector errors** sensor has been added in *.module*. Sends all collector errors and exceptions to the server.
* **Windows OS info/Version & patch** new default sensor has been added.
* **Time in GC** - new computer default sensor has been added (total computer time).
* **"Process time in GC** new process default sensor has been added.

## Sensor changes
* Default prediction logic for **Free disk space prediction** has been changed. Now it uses speed EMA. Default alerts have been removed.
* Preformance counter category for all disks sensors have been changed from **Physical Disk** to **Logical disk**.
* All bar alerts have been changed from **Mean** to **EMAMean** with 5 min **Confirmation period**.

## Sensor settings
* New setting **IsPriorutySensor** has been added. If **IsPrioritySensor** is true, then data skip global message queue and send directly to a HSM seerver.
* New setting **Statistics** has been added. You can swith on server side EMA calculations.

## Alert API

## Bugfixing
* **Windows Info/Last Update** and **Windows Info/Last restart** sensors have been fixed.