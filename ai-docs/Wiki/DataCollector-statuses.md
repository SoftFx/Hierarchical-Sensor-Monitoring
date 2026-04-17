# DataCollector Statuses

This page describes the lifecycle states of the HSM DataCollector.

See also: [HSM DataCollector](HSMDataCollector), [DataCollector Logging](DataCollector-logging)

---

## Collector States

| Status | Switching After... | Actions |
|---|---|---|
| **Starting** | Calling `Start()` method | <ul><li>Server synchronization</li><li>Session creating</li><li>Sensors starting</li><li>Timers initialization</li><li>DataQueue initialization</li></ul> |
| **Running** | Finishing `Start()` method and user custom task (if exists) | Sending user data to the server |
| **Stopping** | Calling `Stop()` method | <ul><li>Sending remaining values</li><li>DataQueue flushing</li><li>Session closing</li><li>Sensors stopping</li><li>Timers stopping</li><li>DataQueue stopping</li></ul> |
| **Stopped** | Finishing `Stop()` method and user custom task (if exists) | — |

## State Map

![image](https://user-images.githubusercontent.com/86781681/235673965-49200eb6-dfb1-4db0-b652-31332ef30979.png)

---

## See Also

- [HSM DataCollector](HSMDataCollector) — Full DataCollector API reference
- [DataCollector Logging](DataCollector-logging) — How to configure logging
- [HSM DataCollector → Lifecycle](HSMDataCollector#lifecycle) — Start/Stop lifecycle details
