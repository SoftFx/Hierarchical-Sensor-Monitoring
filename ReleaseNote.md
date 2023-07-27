# HSM Server

## New Alert constructor has been added
Constructor blocks are divided into 2 parts: **Conditions** and **Actions**
### Condition blocks:
A set of events to perform certain actions. Condition block begins with block **If** and **Property** block that contsains the next options:
* **Value** - select this if you want to create an alert for Intereger or Double sensor value (this property is available only for Integer and Double sensors)
* **Min**, **Max**, **Mean**, **LastValue** - select this if you want to create an alert for bar sensor min, max, mean or last value (this property is available only for bar sensors)
* **Status** - select this if you want to create an alert for sensor status (this property is available for all sensors)

If you select property Value, Min, Max, Mean or LastValue, you should select **Operation** (arithmetic operations **>=**, **>**, **<**, **<>=**) and enter **Target** (numeric value to compare)

If you select property Status, you should select one of 3 available options: **is changed**, **is 🔴 Error** or **is 🟢 OK**

You can combine different conditions by pressing the plus button

### Actions blocks:
A set of events that are executed when certain conditions are met. You can add different types of actions by pressing the plus button or delete unnecessary action by pressing the cross button. There are the next actions:
* **Send notification** - select this if you want to receive telegram notification with custom **Comment template**. This is required action and comment template is also required (default value is $sensor $operation $target)
* **Show icon** - select this if you want to set icon in tree and receive telegram notification with this icon
* **Set 🔴 Error status** - select this if you want to change sensor status to Error

### Time to live alert

If you want to set TTL you should click Add TTL link and select needed interval and actions. If you want to set TTL = Never you should remove TTL alert

## Alerts
* Default template for send notification action has been added
* **Send test message** has been reworked to Toast

## Site
* Line type for **Integer** chart has been changed
* Parent value for **FromParent** interval control has been added in Edit mode
* Default font size has been decreased to 13 pixels
* Alert icons have been added for all cells in Grid and List and for each node header
* **No data** label has been added for empty nodes and sensors
* **Warning** tree filter has been removed

## Policies
* **All policies have been migrated to new style policies**
* **TTL policy** has been added by default (now you can change comment template and icon for TTL)
* **Status policy** has been added by default to all sensors (now you can disable notifications about status changing)
* **Correct type policy** has been added by default to all sensors

## Telegram
* **TEMPORARY Senstivity has been disabled for all sensors!!!**
* **Min status level** has been removed. Current logic has been moved to **Status policy**
* Comment template has been added for ALL telegram notifications
* Default notification format has been changed to: ***icon* (*count* if more than 1): *comment*** (ex. 🔼(10 times) Value > 100 need to check!!!)


## Tree
* **Edit status** item has been added in context menu for sensors
* **Alert icons** have been added to Tree nodes (except folders)
* Counter for Alert icons has been added. If count more than 9 then *Infinity* icon shows

## Sensors
* **Warning** STATUS HAS BEEN REMOVED. ALL WARNINGS HAVE BEEN MIGRATED TO ERRORS.
* Sensor status has been splitted to 2 parts: SensorStatus (OffTime, Ok, Error, received on the side) and PolicyStatus (user configurable in Alert panel)
* **Cleanup** and **TTL** has been migrated to new Settings collection
* Status change logic for **Muted** sensor has been removed

## Table history
* Spinner for long requests has been added
* Receiving time column has been added
* **Show all columns** button has been added to **Actions** button
* UTC format for all time columns has been added

## Configuration tab 
* Telegram settings have been migrated from DB to appsettings.json file

## Bugfixing
* Empty subnodes visibility for rendering tree has been fixed
* Tree context menu items naming has been fixed
* Node state recalculating (notifications, Grafana) has been fixed
* Precalculated period for IntBar history has been fixed
* Site login with empty local storage has been fixed
* ... and other minor bugfixing

## Other
* Redirect to login page has been added if request has invalid cookie user identity
* Order checking for sensor last value has been added
