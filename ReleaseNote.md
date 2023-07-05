# HSM Server

## Alerts

* Default template has been added

## Charts

* Line type for **Integer** chart has been changed

## Tree
* **Edit status** item has been added in context menu for sensors

## Sensors

* **No data** label has been added for empty nodes and sensors
* Muted sensor status change logic has been removed

## Table history
* Spinner for long requests has been added
* Receiving time column has been added
* **Show all columns** checkbox has been added
* UTC format for all time columns has been added

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