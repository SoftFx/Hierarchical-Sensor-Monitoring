# HSM Server

## New folder/product/node/sensor setting **Default chats** has been added
This is a setting allows you to configure default chats and is inherited by all subnodes. All new alerts will be automatically configured for these chats if they have **Default chats** mode in **Notification** block. The next default chats mode is allowed:
* **Not initialized** - default mode for folders and products without folder. All new alerts in  subtree will not send any notifications and will be marked as **Unconfigurated** (same logic as in older HSM versions).
* **Empty** - all new alerts in subtree will not send any notifications and will **NOT** be marked as **Unconfigurated**.
* **Custom** - is installed automatically if one of the chats connected to the folder is selected.
* **From parent** - default mode for all products in folder, nodes and sensors. Inherits the value of the parent.

## Clean empty nodes logic has been added
If a node doesn't include any sensors and more than **Keep sensor history** setting period have passed since the sensor was created this sensor will be removed. This scanning operation is repeated every hour.

## Migrations
* **EMA(Mean)** alert for **Time in GC** default sensor has been added.
* **Default chats** for all sensors has been migrated from **Not initialized** -> **From parent**.
* **Default chats** for all products in folders and nodes has been migrated from **Not initialized** -> **From parent**.
* Alert **Destination mode** for all alerts from **Not initialized** -> **Default chats**.
* Alert **Destination mode** for all TTL product/nodes/sensors alerts from **Not initialized** -> **Default chats**.

## Panels templates
* **Sensor type** column has been added for scanning results.
* **Error style** and **Errors description** have been added for scanning results.
* New valid symbols have been added: **#** **,** **%**

## Panels
* Folder name for panel source has been added to link (if it exists).
* **Last update time** for panel has been added.
* **Tooltip style** switcher has been added.

## Tree
* Empty sensors have been exclude from **Unconfigurated alerts** calculation.
* Alert icons have been hidden for **Muted** sensors.
* **Mute** logic has been added for multyselect items.

## Sensor chart
* **Open/close** time for bar sensors have been converted to UTC format.
* **X axis** has been fixed for charts with a small amount of data (< 300 points).
* **From-To** logic has been fixed for charts with a small amount of data (< 300 points).
* Warning message has been added is **From-To** period consists more than 4000 points.

## Sensors info
* **Graph** tab for empty sensor after sensor update has been fixed.
* **Journal** tab visibility has been fixed for empty sensors.
* **Journal** tab for **File** sensor has been added.
* **Csv preview**  has been fixed for files with a large number of lines (> 10.000).
* Column sorting has been fixed for **Csv** files.

## Access keys
* **Last usage time** info has been added to table.
* **Last usage IP** info has been added to table.

## Folder/edit product
* **DefaultChats** settings control has been added.

## Alerts
* New **Default chat** mode for **Destination** block has been added. Notifications will be sent to chats that are specified in **Default chats** sensor settings.
* **#default** - special keyword has been added for **DefaultChats** mode in alerts destination settings for import/export logic.

## Notifications
* Order of **TTL -> Ok** notifications has been fixed (it checks order of events raising).
* TTL notifications with **Schedule** logic for **Service alive** sensors has been fixed.
* **#N** - number of repeating notification has been added for TTL notifications with **Schedule** logic.

*Old style:*
```
🕑 [HSM Server]/Clients/Default client/Request per second
🕑 [HSM Server]/Clients/Default client/Request per second
🕑 [HSM Server]/Clients/Default client/Request per second
```

*New style:*
```
🕑 [HSM Server]/Clients/Default client/Request per second
🕑 [HSM Server]/Clients/Default client/Request per second #1
🕑 [HSM Server]/Clients/Default client/Request per second #2
```

## Server self monitoring
All default self monitoring sensors have been improved. Added valid alerts, descriptions, units, cleenup settings. Alse new sensors have been added:

#### Database node
* **Full sensors size statistics** - file sensor, collects all information about sensors in the database. Runs every midnight.
* **Top heaviest sensors** - Top **N** heaviest sensors in the database. Uses information from **Full sensors size statistics** sensor. Runs every midnight.

#### Client node
New node with client Public API request statistics has been added. This node includes 2 type of nodes: 
* **_Total** - with general statistics for all users.
* **_Product_/_KeyDisplayName_/_CollectorName_** - with individual statistics for each user.

Each node includes the next sensors:
* **Clients requests count** - number of requests to server per second.
* **Sensors updates** - number of updated sensors per second.
* **Traffic In** - server request size (KB/sec).
* **Trafic Out** - server response size (KB/sec).

## Appsettings.json file
* New **MonitoringOptions** section has been added. This section includes the next settings:
    * **DatabaseStatisticsPeriodDays** - default value is 1. This is the database scan frequency for **Full sensors size statistics** sensor.
    * **TopHeaviestSensorsCount** - default value is is 10. This is value of top **N** sensors from **Top heaviest sensors** sensor.

## HSM Wiki
* How to install server [guide](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Installation) has been uploaded.

# Datacollector v.3.3.1
* **DefaultChatsMode** has been added in sensor settings. Default value is **FromParent**.
* **AlertDestinationMode** has been added in Alerts API. Default value is **DefaultChats**.

# HSMDataobjects v.3.1.4

* New **AlertDestinationMode** for alerts has been added: **DefaultChats**, **NotInitialized**, **AllChats**.
* New **DefaultChatsMode** for sensors has been added: **FromParent**, **NotInitialized**, **Empty**
* New **EMA(Mean)** alert has been added for **Time in GC** sensor.