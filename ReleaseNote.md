# HSM Server

## New Alert constructor has been added
Constructor blocks are divided into 2 parts: **Conditions** and **Actions**
### Condition blocks:
A set of events to perform certain actions.
<ul>
    <li> <b>Property</b>
    <select class="alert-block alert-select property-select">
        <option selected="selected">Value</option>
        <option>Min</option>
        <option>Max</option>
        <option>Mean</option>
        <option>LastValue</option>
        <option>Status</option>
        <option>Inactivity period</option>
    </select>
        <ul>
            <li>Value - asdasdasd</li>
            <li>Min, Max, Mean, Last value - asdasdasd</li>
            <li>Status - asdasdasd</li>
            <li>Inactivity period - asdasdasd</li>
        </ul>
    </li>
</ul>

### Actions blocks:
A set of events that are executed when certain conditions are met.
*Send notification*, *Change status to Error*, *Set icon*

## Grid
* Alert icons have been added for all cells

## Alerts
* Default template has been added
* **Send test message** has been reworked to Toast

## Site
* Parent value for **FromParent** interval control has been added in Edit mode
* Default font size has been decreased to 13 pixels

## Charts
* Line type for **Integer** chart has been changed

## Policies
* **All policies have been migrated to new style policies**
* **TTL policy** has been added by default (now you can change comment and icon for ttl)
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
* Counter for Alert icons have been added. If count more than 9 then *Infinity* icon shows

## Sensors
* **Warning** STATUS HAS BEEN REMOVED. ALL WARNINGS HAVE BEEN MIGRATED TO ERRORS.
* Sensor status has been splitted to 2 parts: SensorStatus (OffTime, Ok, Error, received on the side) and PolicyStatus (user configurable in Alert panel)
* **Cleanup** and **TTL** has been migrated to new Settings collection
* **No data** label has been added for empty nodes and sensors
* Status change logic for **Muted** sensor has been removed

## Filters
* **Warning** filter has been removed

## Table history
* Spinner for long requests has been added
* Receiving time column has been added
* **Show all columns** checkbox has been added to **Actions** button
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

<style>
.alert-block {
    border-radius: 5px;
    height: 1.5rem;
    border: none;
    margin-right: 2px;
    display: inline-block;
}

.alert-text-block {
    padding: 0px 7px;
    border-radius: 5px;
    height: 1.5rem;
    margin-right: 2px;
}


.alert-select {
    cursor: pointer;
    display: inline-block;
}

.alert-select:focus {
    outline: none;
}

.alert-select option {
    background-color: white;
}


.property-select {
    background-color: rgba(51,122,183,.3);
}

.property-select:hover {
    background-color: rgba(51,122,183,.5);
}


.operation-select {
    background-color: rgba(92,184,92,.3);
}

.operation-select:hover {
    background-color: rgba(92,184,92,.5);
}


.target-input {
    background-color: rgba(221,221,221,.5);
    padding: 0px 5px;
    display: inline-block;
}

.target-input:focus {
    outline: none;
}


.target-value {
    max-width: 108px;
}

.target-period {
    max-width: 180px;
    padding: 0px 2px;
}

.target-custom-period {
    background-color: rgba(238,238,238,1);
}


.target-comment {
    min-width: 200px;
}
</style>
