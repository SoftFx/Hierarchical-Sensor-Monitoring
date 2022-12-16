# HSM Server

## Site

* Tree rendering has been improved
* All Access keys tables have been improved
* Logic for Block/Unblock for Access keys has been added

## Core:

* Supporting of old sensor history databases (MonitoringData_ folders) has been removed. **(It's a breaking change)**
* Supporting of sending sensor values by product ID has been removed. **(It's a breaking change)**
* Supporting of file sensors with string content has been removed. **(It's a breaking change)**

## Rest API

* Access key has been moved from request Body to Header. **(It's a breaking change)**
* Old /file request for file sensor with string content has been removed. **(It's a breaking change)**
* /fileBytes request has been renamed to /file. **(It's a breaking change)**
* /listNew request has been marked as Obsolete

## Swagger

* Swagger has been updated to v3
* Key is required header field for every request

## Other

* If there is some exception while sending sensor values, log message contains information about request Access Key
* Bugfixing & optimization

# HSM DataObjects

* Nuget package has been updated to v.3.0.0
* Obsolete properties and classes have been removed. **(It's a breaking change)**
* Property 'key' in requests has been marked as Obsolete
* Property 'value' in FileSensorValue has been redone from byte[] to List<byte>

# HSM Datacollector

* Nuget package has been updated to v.3.0.0
* Using obsolete classes has been removed
* New API method for creating FileSensorValue from string has been added