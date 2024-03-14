# HSM Server

## RATE - New sensor type has been added
New sensor type has been added. Current sensor displays *events count per sec* data in double format. Datacollector contains templates for **Rate** sensors, which send data to server every 1 or 5 minetes. During this period sensor collects and saves information about different events (like requests count, responses, errors etc.) and then sends count of events divided by period in seconds.

## Alerts migration
All **EMA alerts** for **Integer**, **Double**, **IntegerBar**, **DoubleBar** sensors from **.computer** and **.module** nodes have been migrated to scheduled alerts with default period of **1 hour** and **instand send** setting.

## Notifications
* New single notification before schedule groupping has been added. Current logic works only if **instant send** is **True**.
* Notification groupping by **Schedule mode** has been added.

## Scheduled alerts
* New block **and instant send** has been added (send first message before groupping logic).
* New predefined values have been added: **5 minutes**, **10 minutes**, **15 minutes** and **30 minutes**.

## Import/export alerts
* **ScheduledInstantSend** logic has been added.

## Dashboards
* Update interval has been increased from **30 sec** to **2 minutes**.
* New display mode **One column** has been added.

## Panels
* Plot updates have been aggregated to 1 panel update request.
* Strict borders for Y axis have been added.
* Y axis settings for source with **Count** property has been fixed.
* Multithread update for high load panel has been fixed.

## Tree
* Special icon for sensors with unconfigured alerts (with *send notification* action and *empty chats*) has been added.
* Filtering sensors with unconfigured alerts has been added to Filters block (Alerts -> Without chats).

## Journal
* **Show entries** list has been fixed.
* **Search** disabling has been fixed.

## Rest API
* New sensor type **Rate** has been added to API.
* **Instant send** first message logic has be added for schedule alerts.

# Datacollector v.3.3.0

## Reconnection logic has been improved
* Retry logic for failed requests has been added. For data requests max count of failed requests is **10 items**, for command requests - **1000 items**.
* **Progressive delay** between failed requests has been added. Start value is **2 sec** max value is **2 minutes**.
* If previous request is in retry loop, current request is stored in local failure queue.
* After final request attempt, current data will be skipped.
* **Guid** for all requests has been added.
* Log logic for error requests has been improved.

## Collector logic
* **Module name** has been added as **Client name** to HEAD of all requests.
* **IsPrioritySensor** logic has been added. If it's priority sensor then send data logic skips synchronization queue and all sensor data are sent to server as independent request.

## Default sensors
* All EMA alerts for **Integer**, **Double**, **IntegerBar**, **DoubleBar** sensors have been migrated to scheduled alerts with default period of **1 hour** and **instant send** setting.
* **Confirmation period** has been removed for all schedule alerts.
* **Windows last update** sensor has been fixed. It reads data from PowerShell comand.
* **Keep sensor history** setting has been updated to **5 years** for *.module/Version* and *.module/Collector version* sensors.
* **TTL** setting has been updated to **Never** for *Windows errors logs* and *Windows warnig logs*.
* **Post and collect** time info has been added for all default bar sensors to description.

## Alert API
* New setting **ScheduledInstantSend** for scheduled alerts has been added.
* **Client name** property for **BaseSensorValue** has been removed.

## New sensors
* **Avr disc write speed** sensor has been added.
* **Connection Failures Count** sensor has been added to *.computer/Network* node.
* **Connections Established Count** sensor has been added to *.computer/Network* node.
* **Connections Reset Count** sensor has been added to *.computer/Network* node.
* **CreateRateSensor**, **CreateM1RateSensor** and **CreateM5RateSensor** - new tempaltes for **Rate** sensors have been added.
* **Windows Error Logs (Application)** sensor has been added to *.computer/Windows OS info* node.
* **Windows Error Logs** has been renamed to **Windows Error Logs (System)** for *.computer/Windows OS info* node.
* **Windows Warning Logs (Application)** sensor has been added to *.computer/Windows OS info* node.
* **Windows Warning Logs** has been renamed to **Windows Warning Logs (System)** for *.computer/Windows OS info* node.

## Windows collection
* **AddDiskAverageWriteSpeed** method has been added.
* **AddNetworkConnectionsEstablished** method has been added.
* **AddNetworkConnectionFailures** method has been added.
* **AddNetworkConnectionsReset** method has been added.
* **AddDisksAverageWriteSpeed** facade for all computer disks has been added.
* **AddAllNetworkSensors** facade for all Network sensors has been added.
* **AddErrorWindowsLogs** has been migrated to facade for **AddApplicationErrorWindowsLogs** and **AddSystemErrorWindowsLogs** methods.
* **AddWarningWindowsLogs** has been migrated to facade for **AddApplicationWarnignWindowsLogs** and **AddSystemWarnignWindowsLogs** methods.

## Sensor migrations
* All file sensors have been migrated to modern base. **IFileSensor** public interface has been added. This interface supports 2 methods:
    * Send text as file.
    * Send file by path. 
* All last sensor value sensors have been migrated to modern base. New common method **CreateLastValueSensor\<T\>** has been added. This sensors send data to server only after collector stop event.
* All function sensors have been migrated to modern base. **CreateFunctionSensor\<T\>** and **CreateValuesFunctionSensor\<T, U\>** common methods have been added.
    * **INoParamsFuncSensor** - calls set function and sends value to a server by timer.
    * **IParamsFuncSensor** - saves all data in local cache. Calls set function by timer and converts all local data to some value. After sending the data, local storage is cleared.
* All old obsolete classes have been removed. **Obsolete** tag for some methods has been removed.

# HSM DataObjects v.3.1.3

* New sensor type **Rate** has been added.
* New setting **ScheduledInstantSend** has been added for update alert request.
* New predefined periods **5 minutes**, **10 minutes**, **15 minutes** and **30 minutes** for AlertRepeatMode have been added.