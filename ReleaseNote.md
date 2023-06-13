# HSM Server

## New sensor setting **Keep sensor history**
* History for what period should be stored in the database
* For folders and root products default value is **1 month**
* For nodes and sensors default value is **From parent**
* Special service is scanning the database every 1 hour and removing old values

## New sensor setting **Remove sensor after inactivity**
* If sensor doesn't update N time it will be removed with sensor history and all it policies
* For folders and root products default value is **1 month**
* For nodes and sensors default value is **From parent**
* Special service is scanning the database every 1 hour and removing old values


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

## Site
* New **Cleanup** section (with *Keep sensor history* and *Remove sensor after inactivity*) has been added in General folder/product/node/sensor info
* **Cleanup** section has been added in Folder edit
* Right horizontal alignment for ? icons have been added in General info

## Table history
* Subscription for table updates has been added
* Default **TO** value has been changed to UtcNow + 1 year
* Label with updates count and button **Refresh** has been added
* Import csv has the same order and columns like in Rest API methods
* **Last value** column has been added for Bar sensors
* Invalid **Time** order has been fixed
* Duplication for rows has been fixed

## Bugfixing

* Timeout notifications after applying OffTime->Ok has been fixed
* Timeout notifications after server restart (if sensor has Expired state before server stopping) has been fixed