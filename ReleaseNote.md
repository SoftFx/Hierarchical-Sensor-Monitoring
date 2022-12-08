# HSM Server

## Site

* Tree rendering has been improved

## Core:

* Supporting of old sensor history databases (MonitoringData_ folders) has been removed. **(It's a breaking change)**
* Supporting of sending sensor values by product ID has been removed. **(It's a breaking change)**
* Supporting of file sensors with string content has been removed. **(It's a breaking change)**

## Rest API

* Old /file request for file sensor with string content has been removed. **(It's a breaking change)**
* /fileBytes request has been renamed to /file. **(It's a breaking change)**

## Other

* Bugfixing & optimization