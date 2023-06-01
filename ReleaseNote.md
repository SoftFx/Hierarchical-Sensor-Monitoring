# HSM Server

## New entity **Data alerts** has been added
* Data alerts check new sensor data and send Telegram notification after certain conditions
* Data alerts are available for Integer, Double, IntegerBar and DoubleBar sensors
* Integer and Double alerts can check **Value** data property
* IntegerBar and DoubleBar alerts can check **Min**, **Max**, **Mean**, **Last value** data properties
* **If Data alert triggers then Sensor status is changed to Error**
* Send test message logic has been added for Data alerts
* Custom comment constructor has been added for Data alerts with the next variables:
```
$product - Parent product name
$path - Sensor path
$sensor - Sensor name
$action - Alert binary operation
$target - Alert constant to compare
$status - Sensor status
$time - Sensor value sending time
$comment - Sensor value comment
$value - Sensor value
$min - Bar sensor min value
$max - Bar sensor max value
$mean - Bar sensor mean value
$lastValue - Bar sensor lastValue value
```

## Multiselect for Tree node has been added (shift + RMB, ctrl + RMB)
* **Remove** item for multiselect context menu has been added
* **Edit** item for multiselect context menu has been added (multiedit for TTL and Sensetivity)

## Telegram
* New icon ↕️ for Data alerts has been added
* Partial ignore (for TG groups has been added)
* Enable/ignore for different groups logic has been added in Tree context menu

## Other
* New sensors subscribe to TG group if there is some telegram groups in product
* Timespan chart default type has been changed from Bars to Line