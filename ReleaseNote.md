# HSM Server

## Telegram chat logic has been reworked
The logic for creating and storing private and group chats has been merged. All telegram chats have been migrated. The next improvements and changes have been done:

* **LOGIC FOR TELEGRAM CHAT CONNECTION HAS BEEN ADDED ONLY FOR FOLDERS!!!**
* New **Edit** telegram chat page has been added
* **Who add Telegram chat** info has been added
* **Invite** logic for TG chats has been improved.
* **Invite** and **Edit** TG chats logic has been added for **Admin** users
* **All chats** logic from Alert constructor has been fixed. It's including private and group chats now
* **Guide** for inviting all types of TG chat has been added
* List of connected chats has been added in **Edit** folder tab
* **/info** command has been reworked. It's including all TG chats settings and connected folders now
* Chart setting synchronization has been called every 5 min

## Search for Tree has been added
New search by name has been added for sensors tree. Search is processed by names of Folders, Products, Nodes and sensors with ignore case insensitive and partial matches. Also the next logic for search has been added:
* **Enter** hotkey for search has been added
* Autoupdate for tree after clearing search
* Auto opening for filtered nodes has been added

## Telegram bot
* **Notifications** filters have been removed
* Default timeout period has been increased to 5 min
* Logging for TG bot has been improved

## Tree
* Filters tips have been moved in tooltip for **Filters** button
* Autocollapsing for tree after refresh request has been fixed

## Sensor history export
* **Comment** wrap has been fixed for sensor history export
* Export with empty cells has been fixed
* Data order has been fixed

## File sensor
* Empty file preview has been fixed
* **Time** property has been fixed for File sensor

## Charts
* **Comment** has been added in tooltip if data has **Error** status
* **Property** names have been added for **Bar tooltip**
* **Aggregated count** tip has been added for aggregated bars
* Support for value **NaN** has been added for double charts
* Labels for axis have been added: **Time** and **Unit**
* Plot type for Bar sensors has been changed from box to bar
* **IsServiceAlive** and **Service status** buttons have been hidden for sensors in **.computer** node
* Y axis resizing has been fixed

## Other
* Product and sensor selection has been fixed for removed entities
* Journal record for manual status change has been improved
