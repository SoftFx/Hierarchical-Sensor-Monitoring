# HSM Server

## Environment database autobackup logic has been added
* Backup of environment database creates every specified period of time (1 hour by default)
* Old backups of environment database clear every specified period of time (3 days by default)
* New section **BackupDatabaseConfig** has been added in appsettings.json file
* New Docker volume for Backup folders has been added

## Alerts Export
* Exporting logic works from a state of current visible tree (including filtering and searching). If search field is empty then all sensors in subtree are available for export
* Telegram chat names for renamed chats have been fixed

## Alerts Import
* New property **Products** has been added in import model. If this property is empty then relative path from the selected node is used, otherwise paths are built from specified product names

## Notifications
* **Status is change** notification has been fixed if there are several errors in a row in a chain

## Alerts
* New variable **$unit** has been added
* New variable **$prevValue** has been added
* New variable **$prevComment** has been added
* Aggregation in a chain logic for **Status is changed** alert variables **$prevValue** and **$prevComment** has been added
* Default desctionation for all alerts is **Empty** instead of **All chats**

## Charts:
* **IntegerBar/DoubleBar** Open and Close time have been added in tooltip