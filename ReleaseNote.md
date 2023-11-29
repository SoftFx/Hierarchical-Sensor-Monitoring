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
* **Shaphot database** has been removed to **Snapshot database**
* **Server layout** database has been added in backup logic

## Sensor info
* **Alerts** have been added to View mode by default
* **TTL FromParent(Never)** has been restored as visible alert

## Alerts
* View for alerts with empty notifications has been improved.

## Table history
* **Last value** and **Count** columns have been swapped

## Other
* Names of removed nodes have been added to dialog window for **Multi removing** logic
* All items **Delete** have been renamed to **Remove**

# HSM Datacollector v.3.2.4

## New default group **Diagnostic sensors** has been added
* **Package content size** sensor has been added in *.module/Queue stats*. This sensor calculate a body size for each package for Collector message queue.
* **Package process time** sensor has been added in *.module/Queue stats*. This sensor calculate a average process time for each value in package for Collector message queue.
* **Values count in package** sensor has been added in *.module/Queue stats*. This sensor calculate a total count of values in each package for Collector message queue.
* **Queue overflow** sensor has been added in *.module/Queue stats*. This sensor calculate a total count of values witch have been removed from Collector message queue without sending to Server.

## Sensor settings
* New setting **IsPriorutySensor** has been added. If **IsPrioritySensor** is true, then data skip global message queue and send directly to a HSM seerver

## New default sensors
* **Collector errors** sensor has been added in *.module*. Sends all collector errors and exceptions to the server.

## Bugfixing
* **Windows Info/Last Update** and **Windows Info/Last restart** sensors have been fixed.