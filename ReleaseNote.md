# HSM Server

## Build

- .NET has been updated to version 6
- Github workflows have been uploaded
- Project file structure has been changed
- Libraries and nugets versioning has been fixed

## Source code

- HSMSensorDataObjects structure has been changed and old models have been marked as obsolete
- New database structure has been added
- Caching for new data has been added
- Base logic for Sensor Policy has been added (ExpectedUpdateInterval and StringValueLength have been converted to policies)
- Memory using for new style sensor data has been reduced by 50%
- Server load time has been reduced by 50%
- Bugfixing and refactoring

*Note: All old sensors will be converted to the new style during the first server start*

# HSM DataObjects

- Nuget package has been updated to v.2.1.32
- Sensor values structure has been changed, but syntactic compatibility has been preserved
- A lot of old classes have been marked as Obsolete

**If you're using HSM DataObjects package, then you need to update it to latest version**

# HSM Datacollector

- Nuget package has been updated to v.2.1.38
- HSM DataObjests package v.2.1.32 has been added
- Now default status for SensorValues is Ok
- Now all bars have correct CloseTime value

**If you're using HSM Datacollector package, then you need to update it to latest version**
