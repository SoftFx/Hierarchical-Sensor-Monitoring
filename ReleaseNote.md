# HSM Server

## Site

* Tree rendering has been improved
* All Access keys tables have been improved
* Logic for Block/Unblock for Access keys has been added
* Buttons 'Previos page', 'Next page' and number of current page have been added for sensor values history table
* Limit for getting sensor values history from database has been increased to 50000 values (for graph and table)
* Real time refreshing for graph and table with sensor values history has been removed
* Remove for sensors has been added
* Remove for nodes has been added
* Toasts for copy logic have been added
## Core:

* Supporting of old sensor history databases (MonitoringData_ folders) has been removed. **(It's a breaking change)**
* Supporting of sending sensor values by product ID has been removed. **(It's a breaking change)**
* Supporting of file sensors with string content has been removed. **(It's a breaking change)**

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