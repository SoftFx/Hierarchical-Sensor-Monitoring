# HSM Server

## Custom notification schedule logic for TTL alerts has been added
* **starting at** and **instant send** schedule settings for TTL alerts have been removed.
* If a sensor is in TTL state and **Schedule period** has passed since the last notification, then the notification will be sent again.
* Migration has been applied to all default sensors wich stored in ***.module*** and ***.computer*** nodes. Default schedule period - **1 hour**.

## Panels
* Strict **Y axis** has been fixed for **Reset axis** and **Zoom** logic.
* Tooltip position has been improved. 

## Alerts
* TTL **From parent (Never)** logic has been added to ignore for **unconfigurate alerts** logic.
* If an alert schedule setting **starting at** is set to the past, this block is hidden in display mode.
* Removing logic has been added for **send notification** block.

## Sensors
* **Create time** has been added for sensors.
* New unit **# per sec** has been added.

## Edit product page
* Ability to rename product has been added. New product name should be uniq.
* Abitily to change description for product has been added.

## Journal
* Null and empty settings for scheduled alerts have been hidden.

## History
* Precision for **Rate** sensors has been added (5 digits).

## Cleanup service
* Removing sensor metainfo logic has been improved. If sensor is empty it is not deleted automatically. Period is calculated from the time the sensor was created.

## Docker Scripts
* **server_run.ps1** and **server_load.ps1** scripts have been merged into one **server_load.ps1** script.
* New script argument **BaseDirectory** has been added. 
   * ***/usr/HSM*** for Unix systems by default.
   * ***C:\HSM*** for Windows systems by default.
* Default value for **Version** argument has been added - ***latest*** tag.

## CI/CD
* ***latest*** tag has been added for release versions of server.

# Datacollector v.3.3.0
* Scheduled TTL with **Hourly** period has been added by default to **Service alive** sensor.

# HSMDataobjects v.3.1.3
* **ValueInSecond** new unit has been added.