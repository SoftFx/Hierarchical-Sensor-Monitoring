# Hierarchical-Sensor-Monitoring
Alternative Monitoring Solution

HSM is a solution, that allows users to collect, store and process different monitoring data from various sources. Besides traditional data sources, there is a DataCollector library, fully compatible with HSM.

![image](https://user-images.githubusercontent.com/43994777/236455407-9c34bbea-c718-46e2-85cb-5eac422f7543.png)

Visit [HSM wiki](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki) to learn more about the project.

# Getting started

Go to [Introduction](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Introduction) page to learn essence of the work of the product by example of connecting the construction of a graph of temperature from open sources.

![image](https://user-images.githubusercontent.com/43994777/229767254-e9cfb412-ebbe-42f9-8ebe-4924c75243ca.png)

# Usage

When working with HSM, the main way to visualize the state of client products is the site. The client can use the data presented in graphical or tabular form. Examples are shown below.
![1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Components/Components4.PNG)
![1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Components/Components5.PNG)

If an erroneous behavior of the sensor is detected on the side of the client application or the server (for example, the sensor is not updated for more than a specified period of time), the client has the opportunity to receive a notification about this in a telegram. Example below:

![1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Components/Components6.png)

Notifications can be both in private messages and in group chats. Thanks to this, the client can be aware of the status of the system from a mobile phone at any time. In the event that some of the client's sensors are in the process of being calibrated/tuned, the client can turn off error alerts for a certain amount of time, or permanently.

![1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Components/Components7.png)
