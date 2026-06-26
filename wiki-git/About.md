# Welcome to the Hierarchical-Sensor-Monitoring wiki!


**Hierarchical Sensor Monitoring (HSM)** - freeware product designed to monitor network usage, sensors (standard and custom), collecting information about the system and drawing up reports. Works in the family of Windows and Linux operating systems.

**Program features:**
* collection of information about data streams passing through specific devices, saving it in the program database;
* data collection via GRPC, https protocols;
* viewing statistics in the database in the form of graphs and tables;
* statistics of transmitted data packets and ping time;
* viewing results in real time or for a certain period of time in the past on different devices;
* collection of data on the load on the memory subsystems and the processor.

The results can be displayed through the program's own graphical interface or in an Internet browser. In addition, a web server is integrated into the program, which allows you to receive data using a remote connection, and the built-in authentication system allows you to monitor the results in multi-user mode. An example of work is below:

Aggregator - a connected product to the HSM system. In this example, we are tracking the connection Aggregator - Liquidity Provider (LP). The aggregator sends the current state to the HSM server, which, in turn, sends this data to the site, for visualization convenience, and to Telegram, to notify customers about the current situation on the product.
![About1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About1.PNG)

At some point, communication with LP was interrupted. The aggregator reports this to the HSM server.
![About2](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About2.PNG)

HSM perceives this as an erroneous situation, changes the status of the product on the site, and sends a notification to the user that the connection with LP is broken.
![About3](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About3.PNG)

The LP connection error will last until the connection is restored and the aggregator sends information about it. As soon as this happens, the HSM server will update the data on the site and send a telegram notification that the connection with LP has been restored.
![About4](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About4.PNG)


