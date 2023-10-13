# HSM Server

## New setting for Alerts - **Sensativity** has been added
Sensitivity is set in TimeSpan format (0.00:00:00 or 00:00:00). When sensitivity is set, notifications are sent with a set delay. Notifications aren't sent if new data has arrived that didn't trigger the alert.

## Import/export alerts
* **Sensativity** has been added for alerts
* Lists of string are deserialized into one line
* Exporting have been fixed for removed sensors

## Sensors
* Rounding by seconds for instant **Singleton** sensors have been added
* **Singleton** reset after sensors update has been fixed

## Tree
* Alert icons have been hidden if sensor has **OffTime** status
* Opening of nodes has been fixed if you select a node in the grid

## Notifications
* Notifications with OffTime status have been fixed (Notifications with OffTime status shouldn't be sent)

## File sensor
* Journal message for empty file has been fixed
* View for empty file has been fixed
* Realtime update for the last value has been fixed

## History
* Date columns size has been fixed
* Pagination lock has been fixed (The lock was left only for file sensors)
* TTL records have been added for Export history
* All hidden column have been added for Export history (if hidden columns are enabled)
* Persistance for hidden columns for paging has been added

## Swagger
* All **Enums** have been changed from int to string

## Bugfixing
* Null value for Version sensor data has been fixed
* **TTL** and **Keep sensor(s) setting** have been fixed for product moving between folders
* **Enable for Grafana** is false has been fixed for **/addOrUpdate** endpoint
* Output for alert errors has been added for **/addOrUpdate** endpoint

# HSM DataCollector 

## v. 3.2.3
* **PostDataPeriod** for **WindowsInfoSensorOptions** have been fixed to 12 hours
* **WithSensativity** block has been added for Alert API
* Double / has been fixed for path with Module settings