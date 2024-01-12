# HSM Server

## Dashboards

* Supporting for plot Properties of bar sensors have been added.
* Plotted properties for datasource have been added. **Value**, **EMA (Value)** for Instanse sensors. **Min**, **Mean**, **Max**, **Count**, **EMA (Min)**, **EMA (Mean)**, **EMA (Max)**, **EMA (Count)** for Bar sensors.
* New time periods have been added: **12 hours**, **1 day**, **3 days**, **7 days**.
* **Label** autoupdate has been added after change **Property** parameter.
* **Color** and **Label** update for datasource has been fixed.
* Multiline realtime update after a few hours has been fixed.
* Plot for double charts with NaN values has been fixed.


## Alerts
* **Schedule** logic has been added. Available periods are: **1 hour**, **1 day**, **1 week**. All notifications are grouped by time and sent according to a set setting. 
* **Add alert** logic - priority check has been added. New alerts cannot be added if alert with highest priority is exists.

## History
* **Predifined periods** for fast searching have been added. Available values: **Default (300 items)**, **Last day**, **Last 3 days**, **Last week**, **Last 2 weeks**, **Last month**.
* All old search logic has been moved to **Custom** item.
* Persistanse for history search has been added. State is saved to browser Local storage.
* Tooltip with help for **Bars count** parametr has been added.

## Sensor
* **Edit** link has been moved to icon and moved to sensor path.
* **Unit** info has been added after path for selected sensor.
* More readable dispaly values for **Units** have been added.
* **EMA** - description in help has been improved.
* **EMA** update via REST API has been fixed.
* **Change status** for empty sensor has been fixed.

## Charts
* Multiple plot for Bar properties has been fixed.
* **Reset** button behavior with long **Service alive** value has been fixed.

## Journal
* Removing **reason** has been added (Cleanup, Manual remove).
* Mute period has been added for journal record.

## Tree
* Context menu style for empty sensors has been fixed.
* Context menu for node without sensors has been fixed.
* **Tree reload** on **Home** page after double click on node has been fixed.
* **Scroll** to selected item after search cleaning has been added.

## Alert constructor
* New block for setting scheduled alerts **scheduled every** with values **Hour**, **Day**, **Week** have been added.
* New block for setting scheduled alerts **starting at** with calendar has been added. Default value - next hour after current date.

## Alert import/export
* New properties for Scheduled alerts have been added.

## Other
* .NET version has been increased to .NET8
* All alerts for default sensors have been migrated to EMA version (for **Value**, **Min**, **Mean**, **Max**, **Count** properties).
* Copyright has been increased to 2024.

# HSM Datacollector v.3.2.5
* **AndSendScheduledNotification** method has been added for Alerts API.

# HSM Datacollector v.3.1.2
* New properties for Scheduled alerts to **AlertUpdateRequest** have been added.
* New enum **AlertRepeatMode** (Hourly, Daily, Weekly) has been added.