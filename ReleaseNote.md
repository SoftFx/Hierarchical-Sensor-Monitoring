# HSM Server

## New entity **Data alerts** has been added
* Data alerts check new sensor data and send Telegram notification after certain conditions
* Data alerts available for Integer, Double, IntegerBar and DoubleBar sensors
* Integer and Double alerts can check **Value** data property
* IntegerBar and DoubleBar alerts can check **Min**, **Max**, **Mean**, **Last value** data property
* **If Data alert triggers then Sensor status will be change to Error**
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

## Telegram
* New icon ↕️ for Data alerts has been added


## New sensors
* New sensors subscribe to TG group if it's enabled for sensors product

## Other
...