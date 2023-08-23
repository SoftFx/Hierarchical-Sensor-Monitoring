# HSM Server

## Alert constructor
* New property **Comment** with **Is change** operation for conditions block has been added
* **Send notification** block has been improved. Now you can select chats to which this alert should be sent.

## Alerts
* Alert chats have been added for ALL ALERTS according to sensor enable/disable settings

## Time to Live
* TTL triggering information has been added to a table with sensor data

## Charts
* Plotly control buttons panel has been filtered
* The logic of building charts by Bar properties (Min, Max, Mean, Count) has been added

## Notifications
* If the TTL is turned off telegram notification includes $status of received value (Error or Ok) and template of TTL alert

## Rest API
* New option **includeTTL** has been added for history requests

## Other
* A tooltip for alert icons has been added if their count is more than 3
* Context menu item **Notifications** has been hidden
* From-To control for Journal tab has been disabled
* Journal tab is hidden if it is empty
* **To** for From-To control by default has been set as UTC.Now + 1 day instead of UTC.Now + 1 year