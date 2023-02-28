# HSM Server

## Site

* Toasts for Copy and Remove logic have been added
* Tree filters has been updated
* New context menu item 'Ignore product/node/sensor' has been added
* Enable/Ignore group notifications has been added
* 'Copy name' context menu item has been added for product in tree

### Tree

* Removal logic for sensors has been added
* Removal logic for nodes has been added

### Edit product

* The guide how to add a bot in a telegram group has been added

### Product Table

* Product table has been refactored
* Last update column has been added
* Filters by Name and Managers have been added
* Sorting by Last update has been added

## Core

* New sensor state 'Ignore' has been added

## Telegram

* Changing statuses aggregation has been added
* Statuses in notification are displaying like icons ✅ (OK), ⚠️ (Warning), ❌ (Error), ⏸ (OffTime)

## Rest API

* New endpoint **/timpsan** for TimeSpan sensor data has been added

## Kestrel

* Bulder has been moved to minimal builder architecture (.Net 6+)
* Appsettings.json has been added for server configuration. The file mounted in Config folder
* Port settings and Certificate settings have been moved to Appsettings.json
* Config.xml file has been removed

## Project:

* Target framework has been updated to .Net 7.0
* Webpack for building client side part of application has been added
* All js and css libraries has been uploaded
* Bootstrap has been uploaded to version 5.2

## Other

* Bugfixing & optimization

# HSM DataObjects

* Nuget package has been updated to v.3.0.1
* New sensor type Timespan has been added

# HSM Datacollector

* Nuget package has been updated to v.3.0.1
* Now datacollector skips bars with Count equals 0

# Docker

* Dockerfile and .dockerignore have been removed (.Net 7 features)
* Container size has been decreased from 300mb to ~220mb