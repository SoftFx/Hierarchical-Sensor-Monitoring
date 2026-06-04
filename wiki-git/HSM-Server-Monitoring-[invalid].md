HSM Server is collecting self monitoring data using the [HSMDataCollector](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/HSMDataCollector) library. The product for self monitoring is called 'HSM Server Monitoring'. Please note, that we do not recommend using the product to collect other monitoring data. If you would like to get more information about your HSM Server performance, you can either create a task in this repository or contribute.

HSM Server Monitoring product has three sub-nodes. There are CurrentProcess, Database and Load.

## CurrentProcess

CurrentProcess node contains three standard HSMDataCollector performance sensors: Process CPU, Process Memory MB and Process Thread Count. Their functionality is described here [Standard DataCollector Sensors](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Standard-DataCollector-Sensors).

## Database

The Database node has three double sensors, all of them are updated once in 5 minutes.

'All database size MB' checks the size of ALL data, stored by the HSM Server on the disk, and writes that size.

'Environment data size MB' collects the size of non-monitoring data (users, products, configuration, etc.)

'Monitoring data size MB' calculates the size of all monitoring data

## Load

The Load node has four sensors, they all are updated every 45 seconds, and, therefore, have 45 seconds period.

'Received data per second KB' -- the sensor measures every incoming request body size.

'Received sensors per second' -- the sensor calculates common amount of received sensors' data.

'Requests per second' -- the sensor counts every http request, received by the server.

'Sent data per second KB' -- the sensor calculates body size of every response sent by the server.