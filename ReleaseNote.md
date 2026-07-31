# HSM Server

## Chats
* Added per-chat sensor usage count badge so operators can see how many sensors feed each chat at a glance.

## Sensors
* Top CPU sensors are now nested under the `.computer` node, matching the parent-node convention used by the rest of the tree.
* Sensor initialization now publishes its `initialized` flag only after the history load completes, with same-thread re-entry guarded — previously a latch-on-failure could lock the sensor into an unreadable state on startup.

## Dependencies
* Bundled `HSMDataCollector` 3.5.0.
