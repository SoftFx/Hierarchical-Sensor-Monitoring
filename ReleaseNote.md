# HSM Server

## Home Sensors

* Service alive fixed incorrect display.
* Service alive now shows when DataCollector was restarted.
* Service alive default history value is set to "3 days".

## Dashboards

* Fixed singlemode inccorrect behaviour when switchin modes.

## DataCollector

* Now Service Alive sends false value when starting.
* Now threadpool sensor shows correct number of threads used.


## Sensor Improvements

* Added sensor creation date
* Now when sensor is created, the record is addded to the journal.
* When sensor is removed, the record will be added to the journal.

## Notification

* Removed '#' from grouping logic.
* Now messages have max length, that equals to 1000 symbols.

## Alerts

* Now possible to select combination of available chats.

## Backup
* Now backup time doesn't depend on previous backup time. 

## Bug fixes

* Fixed timeout(TTL) was having incorrect behaviout after restart.
* Fixed bug, where sensors sometimes duplicate themselves.
* Fixed journal wasn't saving record after node remove.
* Fixed "Empty panel" were behind panel area.
* Fixed wrong Service Alive notifications.
* Fixed NRE on collector stop.