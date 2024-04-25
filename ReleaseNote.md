# HSM Server

## New folder/product/node/sensor setting **Default telegram chat** has been added
This is a setting that allows you to configure default chat. It is inherited by all subnodes. All new alerts will be automatically configured for that chat if they have **Default chat** mode in **send notification** block. The next default chat modes are allowed:
* **Not initialized** - default mode for folders and products without folder. All new alerts in subtree will not send any notifications and will be marked as **Unconfigured** (same logic as in older HSM versions).
* **Empty** - all new alerts in subtree will not send any notifications and will **NOT** be marked as **Unconfigured**.
* **Custom** - this mode is installed automatically if one of the chats connected to the folder is selected.
* **From parent** - default mode for all products in folder, nodes and sensors. The value of the parent will be inherited.

## Cleaning empty nodes logic has been added
If a node doesn't contain any sensors and more than **Keep sensor history** setting period has passed since the node was created, this node will be removed. This scanning operation is repeated every hour.

## Migrations
* **EMA (Mean)** alert for **Time in GC** default sensor has been added.
* **Default telegram chat** for all sensors has been migrated from **Not initialized** to **From parent**.
* **Default telegram chat** for all products in folders and nodes has been migrated from **Not initialized** to **From parent**.
* Alert **Destination mode** for all alerts has been migrated from **Not initialized** to **Default chat**.
* Alert **Destination mode** for all products/nodes/sensors TTL alerts has been migrated from **Not initialized** to **Default chat**.

## Panels templates
* **Sensor type** column has been added for scanning results.
* **Error style** and **Errors description** have been added for scanning results.
* New valid symbols have been added: **#** **,** **%**

## Panels
* Folder name for panel source has been added to link (if it exists).
* **Last update time** for panel has been added.
* **Tooltip style** switcher has been added.

## Tree
* Empty sensors have been excluded from **Unconfigured alerts** calculation.
* Alert icons have been hidden for **Muted** sensors.
* **Mute items for...** logic has been added for items multiselect.

## Sensor chart
* **Open/close** time for bar sensors have been converted to UTC format.
* **X axis** has been fixed for charts with small amount of data (< 300 points).
* **From-To** logic has been fixed for charts with small amount of data (< 300 points).
* Warning message has been added if **From-To** period contains more than 4000 points.

## Sensors info
* **Graph** tab for empty sensor after sensor update has been fixed.
* **Journal** tab visibility has been fixed for empty sensors.
* **Journal** tab for **File** sensors has been added.
* **Csv preview**  has been fixed for files with a large number of lines (> 10.000).
* Column sorting has been fixed for **Csv** files.

## Access keys
* **Last usage time** info has been added to table.
* **Last usage IP** info has been added to table.

## Folder/product edit pages
* **Default telegram chat** setting control has been added.

## Alerts
* New **Default chat** mode for **Destination** block has been added. Notifications will be sent to chats that are specified in **Default telegram chat** sensor settings.
* Special keyword **#default** has been added for **Chats** setting for importing/exporting alerts logic.

## Notifications
* Order of **TTL -> Ok** notifications has been fixed (it checks order of events raising).
* TTL notifications with **Scheduling** logic for **Service alive** sensors have been fixed.
* **#N** - number of repeating notification has been added for TTL notifications with **Scheduling** logic.

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
All default self monitoring sensors have been improved. Valid alerts, descriptions, units, cleenup settings have been added. Also new sensors have been added:

#### Database node
* **Full sensors size statistics** - This sensor sends information about extended database memory statistics that sensors history occupies. It is a file in CSV format that has 5 columns: Product, Path, Total size in bytes (number of bytes occupied by sensor keys and values in the sensors history database), Values size in bytes (number of bytes occupied by sensor values only in the sensor history database), Data count (number of sensor historical records). By default the memory check is carried out every day at midnight.
* **Top heaviest sensors** - This sensor sends information about the top N heaviest sensors (sensors that take up the most database memory) in MB. The memory check is carried out every day at midnight. This sensor uses information from **Full sensors size statistics** sensor.

#### Client node
New node with client Public API requests statistics has been added. This node includes 2 types of nodes: 
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
    * **TopHeaviestSensorsCount** - default value is 10. This is value of top **N** sensors from **Top heaviest sensors** sensor.

## HSM Wiki
* How to install server [guide](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Installation) has been uploaded.

# Datacollector v.3.3.1
* **DefaultChatsMode** has been added in sensor settings. Default value is **FromParent**.
* **AlertDestinationMode** has been added in Alerts API. Default value is **DefaultChats**.

# HSMDataObjects v.3.1.4

* New **AlertDestinationMode** for alerts has been added: **DefaultChats**, **NotInitialized**, **AllChats**.
* New **DefaultChatsMode** for sensors has been added: **FromParent**, **NotInitialized**, **Empty**
* New **EMA (Mean)** alert has been added for **Time in GC** sensor.