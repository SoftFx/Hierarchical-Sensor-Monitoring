# HSM Server

## New instanse **Table of change** has been added
* Table of change has been added for product/sensor **Properties**, **Settings**, **Alerts**
* Table of change saved update time and last change initiator for each object
* An object can only be modified if the Type of the initiator is higher than a initiator of The last change.
* Priority System -> DataCollector -> User
    * **System** - Can only change its own objects
    * **DataCollector** - A collector can only update objects that were last updated by either a collector or system 
    * **User** - A user can change any objects

## Sensors
* New setting **Aggregate data** has been added. If the setting is enable only data that differs in comment, value or status from the previous value is stored in a database.
* New setting **Unit** has been added. The setting shows in which units the sensor values stored.

## Alerts
* **Value** property for TimeSpan sensor has been added
* New variable **$property** has been added.
* $target for **Status OnChange** property returns sensor name instead guid.

## History
* **Last update** and **Aggregation count** columns have been added for sensors with **Aggregate data** setting

## Journal
* Initiator type has been added. (System, DataCollector, User)
* Records about change by parent for all **From parent** alerts have been added
* Never and Forever values have been added to Journal records (instead of None)
* A Journal tab is hidden if the journal is empty

## Manual status change
* Input for **Value** has been added for instant sensors (int, double, string, bool, Version, TimeSpan)
* Checkbox to change last value has been added

## Charts
* A button to display the nearest **Service status** as a background has been added
* A button to display the nearest **Service alive** as a background has been added
* Sensor TTL is displayed as gray dots on the graph

## Rest API
* New endpoint **/addOrUpdate** has been added. This endpoint allows you to configure any sensor through DataCollector
* New endpoint **/commands** has been added. Allows you to batch send commands to the server (*ex. /addOrUpdate*)

## Other
* View format for TimeSpan has been improved. (From 31d 0h 0m 0s -> 31 days)
* System has been added as initiator to Muted->Available update

# HSM DataCollector v. 3.2.0

## Settings
* New setting **Module** has been added. The module is automatically inserted at the start of the paths of all collector sensors

## Sensor settings
* The user can configure all sensors through DataCollector. More information can be found [**here**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/DataCollector-sensor-settings)
* The user can override all default sensors settings.  More information can be found [**here**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/DataCollector-sensor-settings)
* New setting **Unit** (bytes, MB, GB and etc.) has been added to sensor settings

## Default sensors
* Description, alerts, ttl and other settings have been added for all default sensors
* New default sensor **Active disk time** has been added
* **Is need update** sensor has been removed. It's logic has been added as alert to **Last update** sensor

## Alerts API
* Alerts API in fluent style has been added to Datacollector. More info about alerts can be found [**here**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Alerts-constructor). How to use Alerts API examples can be found [**here**](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/DataCollector-sensor-settings)

## Bugfixing
* **Mean**  calculation for Bar sensors has been fixed

# HSM HSM SensorDataObjects 3.0.3
* Requests for sensors settings have been added
* Requests for alerts settings have been added
* Command requests for server settings have been added