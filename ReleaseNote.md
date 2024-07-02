# HSM Server

## Configuration Tab Updates

Cofiguration page got refactored. Old settings are removed. Added new:

* **Server:**
    1) Kestrel settings (Site port, Sensors API Port).
    2) Cetificate settings (Name, Key).
* **Backup:**
    1) Automatic backup settings(Periodicity, Storage time, Enable/Disable).
    2) Sftp connection settings - mainly settings for sft connection sucj as host name, port number, key etc.
* **Self monitoring** - new settings for collecting statistic about haviest sensors.
* **Telegram** - old settings that was moved from previous version, contains Bot name, Bpt token and Disable/Enable button.

Added realtime update for **Backup**, **Self monitoring**, and **Telegram** settings.
Improved *Help* for each parameter(show default value, why this parameter is used etc).
Added manual **backup** button.

## Server

* Added logic for **backup**. Backup is sent every midnight, if failed - try again after some time.
* Fixed server startup when snapshot is corrupted.

## Panels

* **Single mode** added for panels - each panel can be transfered to single mode, that has three columns: Product name, Label, Last value.
* **Version** plot support added for panels. **Single mode** works with **Version** sources.
* Added new relayout logic for **Single mode** panels. This would help positioning more panels in each row.
* Added label **(values are aggregated)** for panel name, if the settings for aggregation is applied.

## Product/Node Info

* **Default telegram chat** property renamed to **Telegram chats**.
* **Telegram chats** now can be multiselected. All combinations can be selected except **Empty** and **Not initialized** values.
* **Telegram chats** are hardcoded for the alerts destination(**Not for sensors**).
* Error is now added to **View** mode.

## Tree

* Added node **Backups** with two sensors: **Local backup size** and **Remote backup size**.
* Fixed pink icon was showed on tree item when aler was **disabled**.
* Fixed modal dialog popup when moving from one sensor/node to other.


## Appsettings.json file

* New **Self monitoring** settings added. This settings is responsible for configuration property **Self monitoring** setting.


# Datacollector v.3.4.1

* Target framework is set to **net8.0; net472**
* **Timer** logic is removed, it is replaced by periodic **Task**.
* Old **queue logic** is reworked for better performance.
* Data transfer is improved for better memory management.
* Now it is possible to add custom **HttpClient** implementation for Datacollector.
