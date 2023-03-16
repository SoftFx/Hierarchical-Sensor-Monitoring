# HSM Server

## Site

* On-site files preview and preview in new tab have been added for file sensors ('.txt' and '.csv' extensions supported)
* If sensor/node/product is *Muted*, *Notifications* in context menu will be hidden for this item
* Sort by Last update has been optimized for Tree
* Status comment (if status is not Ok) has been moved from Tooltip to bottom of a sensor panel. 

## Rest API

* An ability to request the latest sensor history values has been added. You need to use a [from - count] request where count is negative

## Telegram notifications

* Grouping by nodes has been added (ex. ⚠️->❌->✅ [testAggregation]/[Free2X, FXOpen, Binanace1, Binanace2, Binanace3, Binanace4, Binanace5, Binanace6, Binanace7, Binanace ... and 3 more]/Status/IsConnect)
* New command /icons has been added for Telegram bot. The next content will be shown:
```
    ⚠️ - received Warning status
    ✅ - received Ok status
    ❌ - received Error status  
    ⏸ - received Offtime status
    ⌛️ - sensor update timeout
    ❓ - unknown status
```

## Bugfixing

* Displaying *Custom* expected update interval has been fixed in sensor/node/product meta information grid
* Bug when all new empty products have been marked as *Muted* has been fixed
