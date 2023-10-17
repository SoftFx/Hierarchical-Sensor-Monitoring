# HSM Server

## New setting for Alerts - **Alert confirmation period** has been added
Alert confirmation period is TimeSpan (format 0.00:00:00 or 00:00:00). When alert confirmation period is set, notifications are sent with selected delay. Notifications aren't sent if new data has arrived that didn't trigger the alert.

## Import/export alerts
* **Alert confirmation period*** has been added for alerts
* Lists of strings are serialized into one line
* Exporting have been fixed for removed sensors
* Sorting by name has been added for **Sensors** list

## Sensors
* Rounding by seconds for instant **Singleton** sensors have been added
* **Singleton** reseting after sensors update has been fixed

## Tree
* Alert icons hide if sensor has **OffTime** status
* Node opening has been fixed if you select a node in the grid

## Notifications
* Notifications with OffTime status have been fixed (notifications with OffTime status shouldn't be sent)

## File sensor
* Journal message for empty file has been fixed
* View for empty file has been fixed
* Realtime update for the last value has been fixed

## History
* Date column size has been fixed
* Pagination lock has been fixed (the lock is exist only for file sensors)
* TTL records have been added for history exporting
* All hidden columns have been added for history exporting (if hidden columns are enabled)
* Persistance for hidden columns for paging has been added

## Bugfixing
* Null value for Version sensor data has been fixed
* **TTL** and **Keep sensor(s) setting** have been fixed for product moving between folders
* False **Enable for Grafana** has been fixed for **/addOrUpdate** endpoint
* Output for alert errors has been added for **/addOrUpdate** endpoint

# HSM DataCollector 

## v. 3.2.3
* **PostDataPeriod** for **WindowsInfoSensorOptions** have been fixed to 12 hours
* **AndConfirmationPeriod*** block has been added for Alert API
* Double '/' has been fixed for path with Module settings
* Path building for File sensors has been fixed

# HSM DataObjects
## v. 3.1.0
* **Sensitivity** for alert request has been renamed to **ConfirmationPeriod**

# HSM Pinger

## v. 0.0.4
* Master countries (for checking VPN access) has been added
* 10 request aggregated into 1 record statistic
* Description and alerts for sensors have been added
* Country switching and request attempts have been improved