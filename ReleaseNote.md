# HSM Server

## New sensor setting **Keep sensor history**

## New sensor setting **Remove sensor after inactivity**

## Snapshot logic has been added

* Snapshot contains information about previous state of sensor for faster initializing and removing sensor history
* Tree shapshot is saved on disk every 5 mins
* Tree shapshot is saved on disk before server stopping (final snapshot)
* Shaphot helps initializing global state of tree after server starting. If snaphot is not found all databases are scanned

## Tree

* Only visible part of tree is rendered now
* On click loading subnodes has been added
* 200 elements limit per level has been added

## Grid/List

* Pagination has been added
* On click grid and list loading has been added
* Auto add for new sensors has been added
* Auto remove for deleted sensors has been added

## Bugfixing

* Timeout notification after applying OffTime->Ok has been fixed
* Timeout notifications after server restart (if sensor has Expired state before server stopping) has been fixed