# HSM Server

## Dashboards
* Author info for dashboards has been added.
* List of included **Panels** names has been added.
* New period **30 days** has been added.

## Panels
* Checkbox for disabling aggregation has been added.
* Checkbox for autoapply **Product** name to sensor **Label** has been added.
* Logic for hidden plot by legend label click has been added.
* Plot break for **NaN** values has been added.

## Data sources
* Aggregation has been improved. It depends on **Property** value:
   * For **Min** -> Min
   * For **Max** -> Max
   * For **Count** -> Max
   * For **Mean** -> Mean
   * For **Value** -> Mean
   * For **EMA** (all) -> Mean
* Dropdown for line style has been added. Line styles: **linear**, **spline**, **hv**, **vh**, **hvh**, **vhv**.

## Tree Search
* **Match whole word** logic has been added. For activate it you have to add quotes to request (etc. "database").
* Tree save state before filtering. After clear search input previos saved state is restored.
* Freezing and "jumping" for filtered tree have been fixed.
* Input box size has been increased.

## Product/node metainfo
* Calculating sensors size logic has been added.
* Downloading sensor statistics to CSV file has been added.

## Sensor metaifo
* Calculating sensors size logic has been added.

## Edit sensor status
* Last sensor value (and status) after server restart has been fixed.
* **EMA** calculation for updated values has been fixed.
* Fill inputs by Last value for **Value** and **Comment** inputs has been added.
* **Edit status** in context menu has been renamed to **Edit last value**.

## Sensor chart
* **Max** as aggregation function for Bar sensors Count properties has been added.
* X-axis revers for **Custom** predefined period has been fixed.

## Sensor history
* Order of processing data for Bar sensors has been fixed.
* Update for old values (previous week values) has been fixed.
* Order of processing weekly database has been changed (from newest to oldest).

## Notifications
* Connections to Telegram servers with TLS 1.2 protocol has been fixed.

## Self monitoring. Database sensors
* Descriptions and units have been added for all Database sensors.
* New sensor have been added: **Config backups data size**, **Journals data size**
* Some old sensors have been renamed:
    * **Environment data size MB** -> **Config data size**
    * **Monitoring data size MB** -> **History data size**
    * **All database size MB** -> **Total data size**

