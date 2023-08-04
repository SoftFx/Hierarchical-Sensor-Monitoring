# HSM Server

## New Alert constructor has been added
Constructor blocks are divided into 2 parts: **Conditions** and **Actions**. You can add different types of blocks by pressing the plus button or delete unnecessary blocks by pressing the cross button.
### Condition blocks:
A set of events to perform certain actions. Condition block begins with block **If** and may include 3 parts:
1. **Property**
    * **Value** - select this if you want to create an alert for Intereger or Double sensor value
    * **Min**, **Max**, **Mean**, **LastValue** - select this if you want to create an alert for IntegerBar or DoubleBar sensor min, max, mean or last value
    * **Status** - select this if you want to create an alert for sensor status (available for all sensors)
2. **Operation**
    * Arithmetic operations **>=**, **>**, **<**, **<=** (available only if *Property = Value/Min/Max/Mean/LastValue*)
    * Status operations **is changed**, **is 🔴 Error** or **is 🟢 OK** (available only if *Property = Status*)
3. **Target** (numeric value to compare, available only if *Property = Value/Min/Max/Mean/LastValue*)
    * Integer value (for Integer and IntegerBar sensor)
    * Double value (for Double and DoubleBar sensor)

### Actions blocks:
A set of events that are executed when certain conditions are met. Actions block begins with **then** block. There are the next actions:
* **Send notification** - select this if you want to receive telegram notification with custom *Comment template*. This is required action and comment template is also required (default value is $sensor $operation $target)
* **Show icon** - select this if you want to set icon in tree and receive telegram notification with this icon
* **Set 🔴 Error status** - select this if you want to change sensor status to Error

### Special

#### Time to live alert
Time to live alert has special condition with Property = **Inactivity period** and **Interval** target. If you want to set TTL you should click **Add TTL** link and select needed interval and actions. If you want to set **TTL = Never** you should remove TTL alert

## Sensors
* **Warning** STATUS HAS BEEN REMOVED. ALL WARNINGS HAVE BEEN MIGRATED TO ERRORS.
* Sensor status has been splitted to 2 parts: SensorStatus (OffTime, Ok, Error, received on the side) and PolicyStatus (user configurable in Alert panel)
* **Cleanup** and **TTL** has been migrated to new Settings collection
* Status change logic for **Muted** sensor has been removed

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

## Site
* Line type for **Integer** chart has been changed
* Parent value for **FromParent** interval control has been added in Edit mode
* Default font size has been decreased to 13 pixels
* Alert icons have been added for all cells in Grid and List and for each node header
* **No data** label has been added for empty nodes and sensors
* **Warning** tree filter has been removed
* Default template for alerts send notification action has been added
* Alerts **send test message** has been reworked to Toast

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
