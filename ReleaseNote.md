# HSM Server

## New sensor setting **Keep sensor history**

## New sensor setting **Remove sensor after inactivity**

## Snapshot logic has been added

* Snapshot contains information about previous state of sensor for faster initializing and removing sensor history
* Tree shapshot is saving on disk every 5 mins
* Tree shapshot is saving on disk before server stopping (final snapshot)
* After server starting shaphot helps initialize global state of tree. If snaphot not found all database scanning proccess

## Tree

* Only visible part of tree is rendering now
* Loading subnodes by click has been added
* Restriction to 200 elements on level has been added

## Grid/Sensor lists

* Pagination has been added
* Grid or list oading only by click
* Auto add for new sensors has been added
* Auto remove for removed sensor has been added

## Bugfixing

* Timeout notification after applying OffTime->Ok has been fixed
* Timeout notifications after server restart (if sensor has Expired state before server stopping) has been fixed