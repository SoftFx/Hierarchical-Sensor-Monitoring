# HSM Server

## Alert constructor
* New property **Comment** with **Is change** operation for conditions block has been added
* **Send notification** block has been improved. Now you can select charts to which this alert should be sent.

## Alerts
* Alert chats have been added for ALL ALERTS according to sensor enable/disable settings

## Time to Live
* TTL triggering information has been added to a table with sensor data

## Charts
* Plotly control panel has been filtered
* The logic of building charts by Bar properties (Min, Max, Mean, Count) has been added

## Notifications
* If the TTL is turned off message includes $status of received value (Error or Ok)

## Rest API
* New options **includeTTL** has been added for history requests

## Other
* A tooltip for alert icons has been added if their number is more than 3
* Context menu item **Notifications** has been hidden
* From-To control for Journal tab has been disabled
* **To** for From-To control by default has been set as UTC.Now + 1 day instead of UTC.Now + 1 year