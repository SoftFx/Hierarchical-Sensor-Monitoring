## Collector states
| Status | Switching after... | Actions |
| :--- | :--- | :--- |
| Starting | calling Start method |  <ul><li>Server synchronization</li><li>Session creating</li><li>Sensors starting</li><li>Timers initialization</li><li>Dataqueue initialization</li></ul> |
| Running | finishing Start method and user custom task (if exists) | Sending user data to the server   |
| Stopping | calling Stop method | <ul><li>Sending remaining values</li><li>Dataqueue flushing</li><li>Session closing</li><li>Sensors stopping</li><li>Timers stopping</li><li>Dataqueue stopping</li></ul> |
| Stopped | finishing Stop method and user custom task (if exists) | - |


## State map
![image](https://user-images.githubusercontent.com/86781681/235673965-49200eb6-dfb1-4db0-b652-31332ef30979.png)
