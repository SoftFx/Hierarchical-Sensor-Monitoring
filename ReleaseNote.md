# HSM Server

## New alerts constructor has been added
* Constructor blocks are divided into 2 parts: **Conditions** and **Actions**
* **Conditions** - a set of events to perform certain actions.
### Condition types:
```
Status 
Inactivity period
Value (for int/double sensors)
Mean, *Max*, *Min*, *LastValue* (for bars sensors)
```
* **Actions** - a set of events that are executed when certain conditions are met. **Action types**: *Send notification*, *Change status to Error*, *Set icon*

## Alerts
* Default template has been added

## Charts

* Line type for **Integer** chart has been changed

## Tree
* **Edit status** item has been added in context menu for sensors

## Sensors
* **Warning** STATUS HAS BEEN REMOVED. ALL WARNINGS HAVE BEEN MIGRATED TO ERRORS.
* Sensor status has been splitted to 2 parts: SensorStatus (OffTime, Ok, Error, received on the side) and PolicyStatus (user configurable in Alert panel)
* **No data** label has been added for empty nodes and sensors
* Muted sensor status change logic has been removed

## Filters
* **Warning** filter has been removed

## Table history
* Spinner for long requests has been added
* Receiving time column has been added
* **Show all columns** checkbox has been added
* UTC format for all time columns has been added

## Settings 
* Telegram settings have been migrated from DB to appsettings.json file

## Bugfixing
* Empty subnodes visibility for rendering tree has been fixed
* Tree context menu items naming has been fixed
* Node state recalculating (notifications, Grafana) has been fixed
* Precalculated period for IntBar history has been fixed
* Site login with empty local storage has been fixed
* ... and other minor bugfixing

## Other
* Redirect to login page has been added if request has invalid cookie user identity
* Order checking for sensor last value has been added