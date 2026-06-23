# HSM Server

## Alerts

* Inactivity Period is now a condition property inside the regular alert editor. The dedicated TTL section and "Add Inactivity Period" link are removed; every new alert defaults to Inactivity Period and the user can switch back to a regular property.
* Per-node (Folder/Product) alert editor removed. Templates are the only supported path for non-leaf alerting.
* TTL labels renamed to "Inactivity Period" across the UI.
* Atomic AddPolicy / RemovePolicy serialized under the per-product lock — fixes a race on alert group creation.
* TTL orphan-cleanup now gated on the batch initiator only, so mixed-initiator operations no longer drop TTL policies.
* Alert template is preserved on partial DB failure during removal.
* Template mutations are dispatched to each sensor's own queue.
* Node-level TTL From-Parent is now bounded to the node's own Settings.TTL.

## Products

* Duplicate DisplayName rejected on AddProductAsync.
* Rename collision surfaced as a TaskResult error; products are no longer orphaned when a rename target name is taken.

## Sensors

* DB errors during AddSensor are surfaced and partial state is rolled back; a ChangeSensorEvent(Delete) is fired during rollback so the tree stays consistent.

## History API

* History API now returns TimeSpan / Version values directly instead of stringly-typed payloads.
