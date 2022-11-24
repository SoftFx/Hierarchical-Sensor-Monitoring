# HSM Server

## New features:

### Site

* Grid view has been added for nodes and sensors
* List view has been added for nodes
* Meta info view has been added for products and nodes
* Expected update interval has been added for products and nodes (sensor expected update interval is inherited from parent if it doesn't set)

### Sensor statuses

* Unknown status has been replaced by OffTime status (the status indicates that the sensor is off by schedule)
* OffTime status has minimal priority for tree and telegram notifications

## Other

* Bugfixing & optimization

# HSM DataObjects

* Nuget package has been updated to v.2.1.36
* Unknown status has been replaced by OffTime status

# HSM Datacollector

* Nuget package has been updated to v.2.1.42
* Unknown sensor status using has been removed

# HSM Cpp Wrapper

* Unknown status has been replaced by OffTime status