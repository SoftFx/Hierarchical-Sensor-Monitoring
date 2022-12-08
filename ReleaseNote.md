# HSM Server

## Site

* Tree rendering has been improved

## Core:

* Supporting of old sensor history databases (MonitoringData_ folders) has been removed
* Supporting of sending sensor values by product ID has been removed
* Supporting of FileSensorValue has been removed

## Rest API

* There is only one request for receiving file sensors (/file) - receives the value of file sensor, where the file contents are presented as byte array.

## Other

* Bugfixing & optimization