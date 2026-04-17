If you do not want to read the whole article, please proceed to [Limitations](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Sensors#limitations) to avoid possible mistakes;

# Common information

**Sensor** - software tool whose task is to collect, process and provide the information received upon request.

HSM uses two types of sensors:
* **Passive** - collects and broadcasts the information received; 
* **Active** - collects, broadcasts and processes the information received.

When data is sent to the server, two parameters are required. The parameters do not depend on the type of the sensor. The parameters are **Key** and **Path**.

* **Key** - a unique string used to identify the product. Every product has a unique key, which is needed to send the data. Data objects without key will not be stored and passed to clients.

* **Path** - a string of specific format, which shows the path to the sensor inside a tree. A product is a tree root and the sensor is a tree leaf.

**Path** has the following format: "branch1/branch2/branch3/sensor_name". "branch1", "branch2" and "branch3" display the path to the sensor, "sensor_name" is the name of the particular sensor. The string after the last '/' symbol in the path is considered to be the sensor name, if there are no other sensor values with the given name by the given path, a new sensor object is created on the server. Data with incorrect **Path** value is simply ignored.


# Sensor guides

There are some rules you should follow when send monitoring data. The rules are:

* Each sensor is identified by its path. Path must be unique within the product.

* Every time you specify new path then send data, new sensor is created.

* Try to avoid changing the type of an existing sensor, because type check is not performed now.

* When request sensor history, the type of the last value is used. Unconvertable data is ignored.

* To create new node, simply add the node to the path. The node is created automatically.

# Sensor types

There are some predefined sensor types. Each type has its' own data type, and has a different plot presenting it.

* Boolean sensor

Boolean sensor has two possible values: true and false. Booleans are presented as 0 (false) and 1(true) on bar plot.

* Int sensor

Integer sensor collects values of type 'int' (defined by C# language specification as 4 bytes number from -2147483648 to 2147483647). The graph, presenting the sensor, is a simple lines graph.

* Double sensor

Double sensor has same practical value as double sensor. The only difference is about value, Double sensor stores values of 'double' C# type. Different precisions can be specified.

* String sensor

String sensor collects values of type 'string'. There is no special plot type, the only way to view the data is simple data table. 

* BarSensor

There are two BarSensors: IntegerBarSensor and DoubleBarSensor. They work identically. The only difference is the ability to specify precision for DoubleBarSensor, which is applied to all calculations.

BarSensor is a bit more complicated than all the above sensors: it has two timers and values list inside. One timer, which is called 'big timer', specifies the timeout on which BarData is collected. One more timer, small timer, specifies the time for passing intermediate data to the server. Once the timer passes, some numeric characteristics of the stored data are calculated: mean, median, min, max, quartiles and values count. These numeric parameters are stored in the database. BarSensors are plotted as boxplots.

* File sensor

File sensor is a special sensor, which allows you to store different files. IMPORTANT NOTE: only latest file value is stored. The files can be passed to the server in two shapes: string with all file content and bytes array. Extension and filename can be specified and all extensions can be stored. The only limitation is file size, which is given by the biggest size of HTTPS request. All files can be downloaded to client's computer, some of them can be also viewed directly in browser (like html files).

# Limitations

There are now several limits applied to sensors. 
* Sensor path must be unique! Two sensors of different types with same path will be interpreted as same sensor which will lead to data loss and unexpectedly looking plots/tables/files with the data.
* Sensor path nodes is currently limited to 10. This limit may be changed by admins on Admin page.
* HSMDataCollector passes to the server string representations of the collected data (numbers, booleans and bar datas are converted to strings). These strings must not be longer than 1024 characters, all characters after the 2014th are simply trimmed.