# HSM Server

## New tab Journal has been added!

The log records information about who changed some properties and when. Journal saves 4 types of changes:

1. Metainfo changes (Description, Enable for Grafana, Muting)
2. Settings changes (TTL value, Cleanup section)
3. Alert changes (add/remove/update)
4. Sensor remove (only for nodes/products)

Journal has been added for all entities (Folders, Products, Nodes, Sensors). If entity includes some subnodes or sensors their journals merges on one tab and by default load only 100 last records for each sub entities. Also Journal tab includes paging and searching.

## Alerts constructor
* New variable **$prevState** - status of the previous sensor value has been added.
* **Disable/enable** alerts logic has been added

## Notifications
* **Times** part has been moved to the end of the message: 
```
Old style:
(4 times) ❌ [Nike store] Not enough money to buy (99.2$) 

New style:
❌ [Nike store] Not enough money to buy (99.2$) (4 times)
```
* Additional grouping for **$path** variable
```
Old style:
❌ [Nike store][/goods/balls, /goods/hats, /goods/T-shirts] < 10

New style:
❌ [Nike store]/goods/[balls, hats, T-shirts] < 10 
```
* If the TTL is turned off, a message sends
```
Old style:
🕑 [HSM Server Monitoring]/Database/Environment data size MB

New style:
🕑 [HSM Server Monitoring]/Database/Environment data size MB
✅ [HSM Server Monitoring]/Database/Environment data size MB
```

## Alerts
* All OnChange Status alerts that start with $status variable have been migrated to **$prevStatus->$status** template
* Default template for Value alerts has been changed to __[$product]$path $operation $value__

# HSM Datacollector

## v. 3.1.7
* [**Status code**](https://learn.microsoft.com/en-us/dotnet/api/system.net.httpstatuscode?view=net-7.0) for **TestConnection()** method has been added
* Calculation for percentiles has been removed. Percintiles is constant values.
* **NaN** serialization has been added for Double and BarDouble values
* Unblocked file stream for **SendFileAsync** method has been added

## v. 3.1.8
* New default sensor **Service commands** has been added
* New default sensor **Service status** has been added (Windows only!)
* Logic of custom functions for creating sensors has been converted into a new format

## v. 3.1.9
* **AddPartial** method for IBarSensor has been added
* Null ref for custom sensors if Value is null has been fixed