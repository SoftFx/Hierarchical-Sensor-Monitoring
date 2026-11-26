# HSM Server

## Table sensors (new, ADR-0021)
* A new sensor type: rows of a time and nullable integer/double columns, sent by collectors in compressed blocks on a second WebSocket of the collector session (managed collector 4.1.0-preview, native collector 0.15.2, C++ wrapper 2.0.0).
* The server stores blocks at once and packs them hourly into week databases by history period; retention by whole weeks; a changed period moves the rows in the background; removing a table sensor or a product deletes its rows.
* Sensor page: chart with column and range choice, CSV download; REST `POST api/sensors/tableHistory`.
* Self-monitoring: feed sensors `.tables/...` per product and `Database/Tables` in HSM Server Monitoring.
* Settings in the `TableSensors` section (all with defaults). Docs: wiki pages "Table Sensors" and "DataCollector Table Sensors".

## Access control
* Product edit, role and move actions need the product Manager role (#1891); tree changes and alert export/import need Manager; registration grants no role (#1905).
* Access key and folder writes are checked on the entity written (#1898); tree ids resolve through the user's roles, supplied text is shown as text (#1899).

## Tree
* Renaming a node below the root is refused (#1881); a root product's rename or folder change is followed by templates, dashboards and self-monitoring (#1893).
* New products and folders keep history for 3 months by default (#1872).

## Notifications
* Chat and Telegram bot settings changes are journaled (#1875); chat test message shows the delivery outcome (#1841).
* Chats can be changed through the management API and MCP (#1829).

## Dashboards
* Sources a panel subscription adds are stored, guarded and cleaned up (#1896).
