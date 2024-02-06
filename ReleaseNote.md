# HSM Server

## Dashboards
* Author information has been added to dashboards.
* List of included **Panels** names has been added.
* New period **last 30 days** has been added.

## Panels
* Checkbox for disabling aggregation has been added.
* Checkbox for autoapply **Product** name to sensor **Label** has been added.
* Hidden plot by legend label click logic has been added.
* Plot break for **NaN** values has been added.

## Data sources
* Aggregation has been improved. It depends on **Property** value:
   * For **Min** -> Min
   * For **Max** -> Max
   * For **Count** -> Max
   * For **Mean** -> Mean
   * For **Value** -> Mean
   * For **EMA** (all) -> Mean
* Dropdown for line shape has been added. Available line shapes: **linear**, **spline**, **hv**, **vh**, **hvh**, **vhv**.

## Tree search
* **Match whole word** logic has been added. To activate, you should add quotes to the request (ex. "database").
* Saving tree state before filtering. After clearing search input, the previous saved state is restored.
* Freezing and "jumping" of filtered tree have been fixed.
* Search with status filter has been fixed.
* Search with empty sensors has been fixed.
* Search input box size has been increased.

## Product/node metainfo
* Calculating sensors size logic has been added.
* Downloading sensors statistics to CSV file has been added.

## Sensor metaifo
* Calculating sensors size logic has been added.

## Edit sensor status
* Last sensor value (and status) after server restart has been fixed.
* **EMA** calculation for updated values has been fixed.
* Filling inputs by Last value for **Value** and **Comment** inputs has been added.
* **Edit status** menu item in context menu has been renamed to **Edit last value**.

## Sensor chart
* **Max** as aggregation function for Bar sensors Count properties has been added.
* Reversing x-axis for **Custom** predefined period has been fixed.

## Sensor history
* Order of Bar sensors processing data has been fixed.
* Updating of old values (previous week values) has been fixed.
* Order of processing weekly databases has been changed (from newest to oldest).

## Notifications
* Connections to Telegram servers with TLS 1.2 protocol has been fixed.

## Self monitoring. Database sensors
* Descriptions and units have been added for all Database sensors.
* New sensors **Config backups data size** and **Journals data size** have been added
* Some old sensors have been renamed:
    * **Environment data size MB** -> **Config data size**
    * **Monitoring data size MB** -> **History data size**
    * **All database size MB** -> **Total data size**