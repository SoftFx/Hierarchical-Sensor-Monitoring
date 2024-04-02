# HSM Server

## Custom notification schedule logic for TTL alerts has been added
* **starting at** and **instant send** schedule settings for TTL alerts have been removed.
* If a sensor is in TTL state and **Schedule period** has passed since the last notification, then the notification will be sent again.

## TTL alerts for **Service alive** sensors have been migrated to schedule alerts
* Migration has been applied to all default sensors witch stored in ***.module*** and ***.computer*** nodes.
* Default schedule period - **1 hour**.

## TTL alerts
* **starting at** and **instant send** settings have been removed.
* **From parent (Never)** logic has been added to ignore for **unconfigurate alerts** logic.

## Alerts view
* If schedule alert has **starting at** setting in the past, this block is hidden.
* Remove logic has been added for **notifications** block.


## Sensors
* **Create time** has been added for sensors.

## Edit product
* Ability to rename product has been added.
* Abitily to change description for product has been added.

## Cleanup service
* Remove sensor metainfo logic has been improved. If sensor is empty is not deleted automatically. Period is calculated from the time the sensor was created.

## History
* Precision for **Rate** sensor has been added (5 digits).

## Docker Scripts
* **server_run.ps1** and **server_load.ps1** scripts have been merged into one **server_load.ps1** script.
* New script argument **BaseDirectory** has been added. 
   * ***/usr/HSM*** for Unix systems by default.
   * ***C:\HSM*** for Windows systems by default.
* Default value for **Version** argument has been added - ***latest*** tag.


# Datacollector v.3.3.0
* Schedule alerts with **Horly** period has been added by default to **Service alive** sensor.