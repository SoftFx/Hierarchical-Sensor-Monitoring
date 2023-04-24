# HSM Server

## New entity **Folder** has been added:
It allows you to group different products and set the same settings for them (telegram settings, alerts, user roles)

## Site

### Folder/Product/Sensor info tabs
* Folder meta info panel has been added
* Product and sensor meta info panels have been fully redesigned
* Now all description inputs support **Markdown format**
* Default sorting for Grid and List panels has been changed (by status and then by name)

### Tree
* New node type **Folder** has been added
* Context menu names have been uploaded
* **Save and close tree / Restore tree** button has been added
* **Clear history** in context menu has been removed

### Time intervals control
* New value **From parent** has been added. If this value is selected, the parent setting is applied to current entity
* **From parent** setting is available for a product in a folder too.
* Value **Never** is redone. If this value is selected, the current setting is disabled for the entity.
* Control interface has been improved

### Alerts (Policies)
* **Update Expected Interval** has been renamed to **Time to sensor leave**
* New alert **Sensitivity** has been added for sensors. If the sensor doesn`t return to Ok status after the specified time inverval, a notification sends.
* Icons have been added for all alerts

### File Sensor
* A file sensor preview has been improved
* A list of recent sensor values will be shown instead of just the last file value

### Products
* Products tab has been redesigned
* Folders have been added in Products tab
* Modal window for add product has been added
* **Move to...** setting has been added in Product actions (for moving products between folders)

### Bugfixing
* Product Manger rights have been restored
* A sensor will not send the notification about changing state to *Mute* in Telegram
* Telegram /info command will not contain deleted products
* Other minor bugfixing...

## Telegram

* **From parent** for Telegram notifications setting is available from a product in a folder.
* /status command has been renamed to **/server**
* **/status_priority** command has been added
* Icon for Offtime status has been changed to 💤
* Other minor improvements...

## Access Keys
* New permition **CanUseGrafana** has been added (needs for integration with Grafana agent)

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