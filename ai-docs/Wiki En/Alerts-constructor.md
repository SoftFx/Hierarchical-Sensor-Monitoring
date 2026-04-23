Constructor blocks are divided into 2 parts: **Conditions** and **Actions**. You can add different types of blocks by pressing the plus button or delete unnecessary blocks by pressing the cross button.
## Condition blocks:
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

## Actions blocks:
A set of events that are executed when certain conditions are met. Actions block begins with **then** block. There are the next actions:
* **Send notification** - select this if you want to receive telegram notification with custom *Comment template*. This is required action and comment template is also required (default value is $sensor $operation $target)
* **Show icon** - select this if you want to set icon in tree and receive telegram notification with this icon
* **Set 🔴 Error status** - select this if you want to change sensor status to Error

## Special

### Time to live alert
Time to live alert has special condition with Property = **Inactivity period** and **Interval** target. If you want to set TTL you should click **Add TTL** link and select needed interval and actions. If you want to set **TTL = Never** you should remove TTL alert
