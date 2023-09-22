# HSM Server

## Import/Export alerts logic have been added
You can import all alerts from one node and export them to another node. Copy depth is 1 level.  
Exported alert object contains the next properties:
1. Sensors - list of sensor names, that have this alert
2. Conditions - list of alert conditions. Condition has:

    - **Property**: "Status", "Comment", "Value", "Min", "Max", "Mean", "Count", "LastValue", "Length", "OriginalSize", "NewSensorData"
    - **Operation**: "LessThanOrEqual", "LessThan", "GreaterThan", "GreaterThanOrEqual", "Equal", "NotEqual", "IsChanged", "IsError", "IsOk", "IsChangedToError", "IsChangedToOk", "Contains", "StartsWith", "EndsWith", "ReceivedNewValue"
    - **Target**: null for "IsChanged", "IsError", "IsOk", "IsChangedToError", "IsChangedToOk", "ReceivedNewValue" operations and some value for other
3. Template - notification template
4. Icon - tree icon
5. Status - "Ok" or "Error"
6. Chats - list of chats to send alert or null if destination is all chats
7. IsDisabled - true or false

## Alert constructor
* **Value** for **Version** sensor has been added with operations (<, >, <=, >=, =, !=)
* **New data** operation has been added. Alert send message every new value on a sensor.
* **Value** fir **String** sensor has been added with operations (==, !=, contains, startsWith, endsWith)
* **Length** peroperty for **String** sensor has been added (<, >, <=, >=, =, !=)
* **File Size** property for **File** sensor has been added (<, >, <=, >=, =, !=)
* New operations for **Comment** have been added (==, !=, contains, startsWith, endsWith)
* Operations = and != have been added for all number properties

## Tree
* **Sensors count** view has been improved
* **Errors count** view has been added
* Filters for **Sensors count** and **Errors count** have been added
* **Alerts -> Import/Export** items in context menu have been added

## Sensor
* New setting **IsSingleton** has been added. If several Datacolelctors send data to the same path, only first value stored

## Charts
* **Reset** button has been restored on Plotly bar
* Label cheking for Bar properties has been added
* Green color for original **Service alive** chart has been restored 

## Users
* Ability to **change password** has been added in Users tab
* Removing/editing user with specific chars in name has been fixed 

## Other
* Message for removed alerts has been improved for Journal tab
* TimeSpan value view has been improved for notifications

## Bugfixing
* Server crush after reading sensor with set AggregatedValues setting if history is empty has been fixed


# HSM DataCollector 

## v. 3.2.1
* New extension method for readable Timespan value has been added
* New module **Computer name** has been added. Current module contains only global machine sensors like Total CPU, Free RAM, Disks monitoring and etc.
* All default sensors have been splitted into 2 parts: computer and module sensors
* Default process (CPU, RAM, Thread count) sensors have been moved to node with main **process name**
* **IsSingleton** new setting for sensors has been added

## v. 3.2.2
* Count synchronization for PublicBar sensor have been fixed

# HSM SensorDataObjects

## v. 3.0.4
* Alert operations for **String values**, **Comment** and **Receive new data** have been added
* Time units like **ticks**, **milliseconds**, **seconds**, **minutes** have been added