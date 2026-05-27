HSM supports the ability to integrate its data with the Grafana service. Supported data types can be found [here](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Supported-Grafana-datasources).

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/ac9e139e-1769-4034-971b-b670c2915d87)

In order to connect HSM to Grafana, you need to make the following steps:
1. Preparing Grafana to work with HSM (done once on the server);
2. Preparing HSM to work with Grafana;
3. Creating a data source on Grafana;
4. Data integration (creation of dashboards).
***

## Preparing Grafana to work with HSM

The following steps are required to prepare Grafana for HSM integration:
1. LogIn to you Grafana account;
2. Open "Connection" window by Toggle menu;
![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/1eb1029d-57c8-4fb2-9067-5b6bdc00254d)
3. Find "JSON" in connection data;
![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/76a1046a-7204-4be5-89f7-fc7b73bbc0cd)
4. Install "JSON";
![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/2969f782-d862-460f-99dc-ccad06b64066)

This operation is performed 1 time, during the first integration of systems. This completes the preparation for integration.

***

## Preparing HSM to work with Grafana

Working with HSM data is done through **Access Keys**.

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/aee2bb47-061a-4206-9a29-274e25c0f6ea)

There are two types of keys:
1. Master Key - gives access to all server products;
1. Product Key - a specific product key.

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/607eca52-f7e2-4df8-844d-fee5e2a9e08a)

Master Key can be created by admins and used only to communicate with Grafana. For operations not related to Grafana, this key is not valid.

In addition to using the key, you must specify on the site exactly what data should be sent. To do this, right-click on the product/node/sensor whose data we want to display on Grafana, and check "Enable Grafana"

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/6ec2b79d-d4c9-4f9e-a9cc-2528ca61460f)

Next to the product/node/sensor that was selected, a graphana icon will appear, which indicates that data for this entity will be sent (The icon may not be displayed if the visualization of icons is disabled in filters).

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/67b73ec9-8ab5-4f28-b609-07c73e1fc6eb)

Now to visualize the data, you need to copy the product key, or the master key, whose data you want to visualize in Grafana.
***

## Creating a data source on Grafana

1. Click "Data source" by Toggle menu.
2. Click "Add new data source"
3. Choose JSON data source.

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/a7e8fe66-302d-4e91-b3da-2cded2ee5217)

4. Click on the JSON option that appears;

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/8a23c61b-8836-432a-810d-8eb56d396014)

5. Fill URL adress, as example - https://hsm.dev.soft-fx.eu:44333 + /grafana/jsondatasource. Full URL - https://hsm.dev.soft-fx.eu:44333/grafana/jsondatasource;
6. Check "Skip TLS Verify";
7. Click "Add header";

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/a6d03513-0665-42b5-a44f-0ca2e51ea601)

8. Enable checkbox "Skip TLS Verify";

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/17f78de8-91b3-4f03-a05b-63571bb2cdba)


9. Fill **Header** - key and **Value** - use Access Key from HSM (Master or Product);

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/a7ba0c27-6394-43b7-bb59-2b7d5b96f5b9)

10. Click "Save & test"

![image](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/43994777/e56ff703-71fe-4988-a2cb-8b65a77d0621)

Done!

***

## Data integration
 







