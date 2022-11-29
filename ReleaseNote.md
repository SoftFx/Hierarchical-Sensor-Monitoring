# HSM Server

## New features:

### Site

* Grid view has been added for nodes and sensors
* List view has been added for nodes
* Meta info view has been added for products and nodes
* Expected update interval has been added for products and nodes (sensor expected update interval is inherited from parent if it doesn't set)
* Spinners while loading tree and node data have been added

### Sensor statuses

* Unknown status has been replaced by OffTime status (the status indicates that the sensor is off by schedule)
* OffTime status has minimal priority for tree and telegram notifications

## Other

* Max request size has been reduced to 50Mb
* Remorte IP and port logs have been added after Bad request
* Bugfixing & optimization

# HSM DataObjects

* Nuget package has been updated to v.2.1.36
* Unknown status has been replaced by OffTime status

# HSM Datacollector

* Nuget package has been updated to v.2.1.43
* Unknown sensor status using has been removed
* Max sensor's data size for one request has been reduced to 1000 values

# HSM Cpp Wrapper

* Unknown status has been replaced by OffTime status