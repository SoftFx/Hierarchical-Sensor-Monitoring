# HSM Server

## RATE - New sensor type has been added
New sensor type has been added. Current sensor display data in double format as *eventns/sec* data. Datacollector consits templates for **Rate** sensors, whitch send data to server every 1 or 5 minetes. During this period sensor collects and saves information about different events (like requests count, responses, errors etc.) and send count of events divided by period size in seconds.

## Alerts migration
All **EMA alerts** for **Integer**, **Double**, **IntBar**, **DoubleBar** sensors from **.computer** and **.module** node have been migrated to scheduled alerts with default period of **1 hour**.

## Notifications
* New single notification before schedult groupping has been added. Current logic works only if **send instant message** is **True**.

## Scheduled alerts
* New block **and instant send** has been added (send first message before groupping logic).
* New predefine values have been added: **5 minutes**, **10 minutes**, **15 minutes** and **30 minutes**.

## Import/export alerts
* **ScheduledInstantSend** logic has been added.

## Dashboards
* Interval update has been increased from **30 sec** to **2 minutes**.
* New display mode **One column** has been added.

## Panels
* Plot updates have been aggregated to 1 panel update request.
* Strict dorders for Y axis have been added.
* Y axis settings for source with **Count** setting has been fixed.
* Multitrade update for high load panel has been fixed.

## Journal
* **Show entries** list has been fixed.
* **Search** disabling has been fixed.

## Rest API
* New sensor type **Rate** has been added to API.
* **Instant send first message** logic for schedule alerts should be added.

# Datacollector v.3.3.0

## Reconnection logic has been improved
* Retry logic for failed requests has been added. For data request max count of failed requests is **10 items**, for command requests - **1000 items**.
* **Progressive delay** between failed request has been added. Start value is **2 sec** max value is **2 minutes**.
* If previous request in retrying loop, current request saves to local failed queue.
* After final request attempt current data will be skipped.
* **Guid** for all requests has been added.
* Log logic for error request has been improved.

## Collector logic
* **Module name** has been added as **Client name** to HEAD of all requests.
* **IsPrioritySensor** logic has been added. If it's priority sensor then send data logic skips synchronization queue and all sensor data send to server as independent request.

## Default sensors
* All EMA alerts for **Integer**, **Double**, **IntBar**, **DoubleBar** sensors have been migrated to scheduled alerts with default period of **1 hour**.
* **Confirmation period** has been removed for all schedule alerts.
* **Windows last update** sensor has been fixed. It reads data from PowerShell comand.
* **Keep sensor history** setting has been updated to **5 years** for *.module/Version* and *.module/Collector version* sensors.
* **TTL** setting has been updated to **Never** for *Windows errors logs* and *Windows warnig logs*.
* **Post and collect** time info has been added for all default bar sensors to description.

## Alert API
* New block **Send instant message** for scheduled alerts has been added.
* **Client name** property for **BaseSensorValue** has been removed.

## New sensors
* **Avr disc write speed** sensor has been added.
* **Connection Failures Count** sensor has been added to *.computer/Network* node.
* **Connections Established Count** sensor has been added to *.computer/Network* node.
* **Connections Reset Count** sensor has been added to *.computer/Network* node.
* **CreateRateSensor**, **CreateM1RateSensor** and **CreateM5RateSensor** - new tempaltes for **Rate** type sensors have been added.

## Windows collection
* **AddDiskAverageWriteSpeed** method have been added.
* **AddNetworkConnectionsEstablished** method have been added.
* **AddNetworkConnectionFailures** method have been added.
* **AddNetworkConnectionsReset** method have been added.
* **AddDisksAverageWriteSpeed** facade for all computer disks has been added.
* **AddAllNetworkSensors** facade for all Network sensors has been added.

## Sensor migrations
* All file sensors have been migrated to modern base. **IFileSensor** public interface has been added. This interface supports 2 methods:
    * Send text as file.
    * Send file by path. 
* All last sensor value sensors have been migrated to modern base. New common method **CreateLastValueSensor\<T\>** has been added. This sensors send data to server only after collector stop event.
* All function sensors have been migrated to modern base. **CreateFunctionSensor\<T\>** and **CreateValuesFunctionSensor\<T, U\>** commond methods have been added.
    * **INoParamsFuncSensor** - calls set function and send value to a server by timer.
    * **IParamsFuncSensor** - saves all data in local cache. Calls set function by timer and converts all local data to some value. After data send local storage is cleared.
* All old obsolete classes have been removed. **Obsolete** tag for some methods have been removed.