# HSM Server

## New entity **Templates** for Panels has been added
Templates on Panel allow you to quickly create and configure sensor sources. Template consists of 2 parts Filters and Source settings. 
Filter part includes 2 filters:
* **Folders** filter - allows you to select in which folder you want to search for sensors. Supports multiselect logic. Default value is **Any**.
* **Path** tempalte - sensor path template to be added to Panel. It supports 2 types of variables that help to create sensor path templates. The variable supports letters, digits and symbols _ . $  
Variables:
    * **\*** - unnamed;
    * **{piece}** - named variable (can be used in **Label** input).  
  
   ***Example**. Folder filer = **Any**, Path template = **\*/Database/{db_name} size**. It means that all sensors will be added from any folder in any product where there is a **Database** node and the sensor name ends with **size**. Database name saved in **{db_name}** variable.*  

Source settings:
* **Label** - label for new source. It can use **Path template** variables.  
   ***Example**. Path template = **\*/Database/{db_name} size** sensor found by templates **Main product/Database/Journal size**. If Label is **{db_name} folder** the source name will look like **Journal folder**.*  
* **Property** - selected property for all sensors.
* **Shape** - default shape for all sources.

**How to create and apply panel Tepmplate**:
1. Select **Panel**.
1. Add a new template by clicking on the **+Add** button next to the **Tempaltes** title.
1. Сonfigure template fields.
1. After configurationg the template, you need to click **Apply and Enable** item in a contex menu.
1. The logic for scanning the existing sensors starts. If everything is configured correctly, click on the **Enable** button.
1. If you want to run existing sensors scan again with new settings you need to click **Rebuild** button in the context menu.
1. After the scanning logic, the template subscribes to updates from new sensors. If the new sensor matches the template, it will be added automatically with the configured **Source settings**.

## Dashboards
* Dropdown with all Dashboards entities has been added to **Dashboards** tab.
* Autoupdate each 30 sec for Panel legend has been removed.

## Panels
* New logic with fixed Y boarders has been added. Consists of 3 items:
   * Autoscale (checkbox) - default value is true. A chart adapts to **Value** of a points on the chart.
   * Min Y - lower bound of chart **Value**
   * Max Y - upper boid of chart **Value**

  If **Value** has been updated by Y boarders, **Original value** has been added to the point tooltip.
* A limit on the maximum number of sources has been added. Max Source count **is 100**.
* A limit for uniq Id for Source sensor has been remove. (You can add the same sensor with different **Properties**) 

## Panel sources
* **Remove all sources** item has been added in a context menu.
* **Shape** help link opens in a new tab.
* Spase trimming for **Path** and **Label** properties has been added.

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