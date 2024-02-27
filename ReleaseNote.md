# HSM Server

## New entity **Template** for Panels has been added
Panel templates allow you to quickly create and configure sensor sources. Template consists of 2 parts: Filter and Source settings.  

Filter part includes 2 parameters:
* **Folders** filter allows you to select in which folder you want to search for sensors. Supports multiselect logic. Default value is **Any**.
* **Path** template - sensor path template to be added to the Panel. It supports 2 types of variables that help to create sensor path templates. The variable supports letters, digits and symbols _ . $  
Variables:
    * **\*** - unnamed;
    * **{piece}** - named variable (can be used in **Label** input).  
  
   ***Example**. Folder filer = **Any**, Path template = **\*/Database/{db_name} size**. It means that all sensors will be added from any folder in any product where there is a **Database** node and the sensor name ends with **size**. Database name saved in **{db_name}** variable.*  

Source settings:
* **Label** - label for new source. It can use **Path template** variables.  
   ***Example**. Sensor **Main product/Database/Journal size** found using path template = **\*/Database/{db_name} size**. If Label is **{db_name} folder** the source label will look like **Journal folder**.*  
* **Property** - selected property for all sensors.
* **Shape** - default shape for all sources.

**How to create and apply panel Template**:
1. Select **Panel**.
1. Add new template by clicking on the **+Add** button next to the **Tempaltes** title.
1. Сonfigure template fields.
1. After configuring the template, you need to click **Apply and Enable** item in template context menu.
1. The logic for scanning existing sensors starts. If everything is configured correctly, click to the **Enable** button.
1. If you want to run scan again for existing template with new settings, you need to click **Reapply** button in template context menu.
1. After the scanning logic, the template subscribes to updates from new sensors. If the new sensor matches the template, it will be added automatically with the configured **Source settings**.

## Dashboards
* Dropdown with all Dashboards entities has been added to **Dashboards** tab.
* Autoupdate each 30 sec for Panel legend has been removed.

## Panels
* New logic with fixed Y boarders has been added. It consists of 3 items:
   * Autoscale (checkbox) - default value is true. A chart adapts to **Value** of a points on the chart.
   * Min Y - lower bound of chart **Value**
   * Max Y - upper bound of chart **Value**

  If **Value** has been updated by Y boarders, **Original value** has been added to the point tooltip.
* Limit on the maximum number of sources has been added. Max sources count **is 100**.
* Limit for uniq Id of source sensor has been removed. (You can add the same sensor with different **Properties**) 

## Panel sources
* **Remove all sources** item has been added in Source context menu.
* **Shape** help link opens in a new tab.
* Space trimming for **Path** and **Label** properties has been added.

## Notifications
* TTL notification triggering after change **Last sensor value** has been fixed.
* TTL recalculation after TTL policy update has been fixed.

## Sensor metainfo
* New format for database statistics has been added.

## Rest API
* **Client name** for data requests has been added. Needed to identify different collector instanses/clients.

## Infrastructure
* Base security TLS protocol for **Telegram Api** has been uploaded to v.1.2
* All npm packages have been uploaded to Node.js v.20