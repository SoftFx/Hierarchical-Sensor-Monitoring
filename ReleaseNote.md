# HSM Server

## New logic of autobackup Enviroment DB has been added
* Enviroment database create a backup every specified period of time (1 hour by default)
* New section **BackupDatabaseConfig** has been added in appsettings.json file
* New Docker volume for Backup folders has been added

## Alerts Export
* Export works from a state of a current visible tree (including filtering and searching). If a search is empty then all sensors in a subtree are available for export
* Talagram chat names for renamed chats have been fixed

## Alerts Import
* New property **Products** has been added in import model. If is property is empty, then relative path from the selected node is used, otherwise paths are built from a specified product names

## Alerts
* New variable **$unit** has been added
* New variable **$prevValue** has been added
* New variable **$prevComment** has been added
* Aggregation in a chain logic for **Status is change** alert from **$prevValue** and **$prevComment** variables has been added
* Default desctionation for all alerts is **Empty** instead of **All chats**

## Charts:
* **IntegerBar/DoubleBar** Open and Close time have been added in tooltip

## Other
* **IntegerBar/DoubleBar** current time put in "From" field when user click on sensor has been fixed