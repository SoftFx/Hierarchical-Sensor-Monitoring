# HSM Server

## Telegram chat logic has been reworked
The logic for creating and storing direct and group chats has been merged. All telegram chats have been migrated. The next improvements and changes have been done:

* **TELEGRAM CHAT CONNECTION LOGIC IS AVAILABLE ONLY FOR FOLDERS!!!**
* **Edit** telegram chat page has been added
* **Who add Telegram chat** info has been added
* **Invite** logic for telegram chats has been improved.
* **Invite** and **Edit** telegram chats logic has been added for **Admin** users and **folder Managers**
* **All chats** logic from Alert constructor has been fixed. It's including direct and group chats now
* **Guide** for inviting all types of telegram chat has been added
* List of connected chats has been added in **Edit** folder page
* **/info** command has been reworked. It's including all telegram chats settings and connected folders now
* Chat settings synchronization has been called every 5 min

## Search for Tree has been added
New search by name has been added to tree. Search is processed by names of folders, products, nodes and sensors with ignore case insensitive and partial matches. Also the next logic for search has been added:
* **Enter** hotkey for search input
* Autoupdate for tree after clearing search
* Autoopening for filtered nodes has been added

## Telegram bot
* Default timeout period has been increased to 5 min
* Logging for TG bot has been improved

## Alerts
* Full tree depth for **Import/Export** alerts has been added
* Confirmation period for **Status is change** has been reworked. Notifications removed if status get back to the first state of a period
* Icon aggregation for **Status is change** notification has been added. Aggregation aplies only if **$prevSatus** variable has been added to message template.  

*Old style:*
```
✅->❌ [AggrST2]/Bridge/Lp Connections/st2/Connection Status = Disconnected (2 times)
❌->✅ [AggrST2]/Bridge/Lp Connections/st2/Connection Status = Connected (2 times)
```

*New style:*
```
✅->❌->✅->❌->✅ [AggrST2]/Bridge/Lp Connections/st2/Connection Status = Connected
```

## Tree
* **Notifications** filters have been removed
* Enabled/Ignored notifications icons have been removed
* Filters tips have been moved in tooltip for **Filters** button
* Autocollapsing for tree after refresh request has been fixed

## Exporting sensor history
* **Comment** wrap has been fixed for exporting sensor history
* Exporting with empty cells has been fixed
* Data order has been fixed

## File sensor
* Empty file preview has been fixed
* **Time** property has been fixed

## Charts
* **Comment** has been added in tooltip if data has **Error** status
* **Property** names have been added for **Bar tooltip**
* **Aggregated count** tip has been added for aggregated bars
* Supporting of **NaN** values has been added for double charts
* Labels for axis have been added: **Time** and **Unit**
* Plot type for Bar sensors has been changed from box to bar
* **IsServiceAlive** and **Service status** buttons have been hidden for sensors in **.computer** node
* Y axis resizing has been fixed

## Other
* Product and sensor selection has been fixed for removed entities
* Journal record for manual status change has been improved
