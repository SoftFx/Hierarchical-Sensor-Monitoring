# HSM Server

## Alert constructor
* **Value** for **Version** sensor has been added with operations (<, >, <=, >=, =, !=)
* **New data** operation has been added. Alert send message every new value on a sensor.
* **Value** fir **String** sensor has been added with operations (==, !=, contains, startsWith, endsWith)
* **Length** peroperty for **String** sensor has been added (<, >, <=, >=, =, !=)
* **File Size** property for **File** sensor has been added (<, >, <=, >=, =, !=)
* New operations for **Comment** have been added (==, !=, contains, startsWith, endsWith)
* Operations = and != have been added for all number properties


## Sensor
* New setting **IsSingleton** has been added. If several Datacolelctors send data to the same path, only first value stored

## Charts
* **Reset** button has been restored on Plotly bar
* Label cheking for Bar properties has been added
* Green color for original **Service alive** chart has been restored 

## Other
* Message for removed alerts has been improved for Journal tab


# HSM DataCollector 

## v. 3.2.1
* New extension method for readable Timespan value has been added
* New module **Computer name** has been added. Current module contains only global machine sensors like Total CPU, Free RAM, Disks monitoring and etc.
* All default sensors have been splitted into 2 parts: computer and module sensors
* Default process (CPU, RAM, Thread count) sensors have been moved to node with main **process name**
* **IsSingleton** new setting for sensors has been added

## v. 3.2.2


# HSM SensorDataObjects

## v. 3.0.4
* Alert operations for **String values**, **Comment** and **Receive new data** have been added
* Time units like **ticks**, **milliseconds**, **seconds**, **minutes** have been added