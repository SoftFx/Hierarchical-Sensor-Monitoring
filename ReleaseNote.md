# HSM Server

## New tab Journal has been added!

The log records information about who and when changed some properties. Journal saves 4 types of changes:

1. Metainfo changes (Description, Enable for grafana, Mute)
2. Settings changes (TTL value, Cleanup section)
3. TTL changes (add/remove/update)
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
* If the TTL is turned off, a message will be sent
```
Old style:
🕑 [HSM Server Monitoring]/Database/Environment data size MB

New style:
🕑 [HSM Server Monitoring]/Database/Environment data size MB
✅ [HSM Server Monitoring]/Database/Environment data size MB
```

## Alerts
* All OnChange Status alerts witch starts with $status variable have been migrated to **$prevStatus->$status** template