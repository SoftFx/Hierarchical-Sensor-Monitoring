# HSM Server

## Site

### Product/Sensor info tabs
* Tabs with Product and Sensor info have been fully redesigned
* Now all description inputs support **Markdown format**

### Tree
* Context menu names have been uploaded
* **Save and close tree / Restore tree** button has been added
* **Clear history** in a context menu has been removed

### Time intervals control
* New value **From parent** has been added. If this value is selected, the parent setting is applied to current entity
* Value **Never** is redone. If this value is selected, the current setting is disabled for the entity.
* Control interface has been improved

### Alerts (Policies)
* New alerts **Sensitivity** has been added for sensors. If the sensor doesn`t return to Ok status after the specified time inverval, a notification sends.
* Icons have been added for all alerts

### File Sensor
* A file sensor preview has been improved
* A list of recent sensor values will be shown instead of just the last file value

### Bugfixing
* Product Manger rights have been restored
* A sensor will not send the notification about changing state to *Mute* in Telegram
* Other minor bugfixing...

# HSM Datacollector

### Structure and optimizations
* Async requests and handlers for HttpClient have been added
* Base structure for **Simple sensor** (a sensor that sends data on user request, not on a timer) has been added

### Default sensors
* **CollectorAlive** sensor has been renamed to **CollectorHeartbeat**. Sensor name has been renamed from **Service alive** to **Service heartbeat**
* New default sensor **Product info** has been added. Now it contains Product Version with Version start time

### New methods
* New method **SendFileAsync** has been added

### Other
* Collector version has been increased to 3.1.0