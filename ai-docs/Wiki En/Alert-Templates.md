# Alert Templates

Alert Templates let you define a set of alert policies once and apply them automatically to multiple sensors based on a **path pattern** and **sensor type**.

Instead of manually configuring alerts on each sensor, you create a template — and any sensor that matches the template's path and type inherits its policies automatically.

---

## When to Use Templates

- You have many sensors with the same monitoring requirements (e.g., all CPU sensors should alert above 90%)
- You want to enforce consistent alert rules across a product or folder
- You add new sensors regularly and want them configured automatically

---

## Managing Templates

Go to **Alerts → Templates** in the HSM Web UI.

Templates are **scoped to a Folder** — they apply only to sensors in products belonging to that folder.

---

## Template Fields

| Field | Description |
|---|---|
| **Name** | Display name for the template |
| **Folder** | Which folder's sensors this template applies to |
| **Path pattern** | Glob-style pattern to match sensor paths |
| **Sensor type** | The sensor type to match, or **Any type** |
| **Alert policies** | One or more conditions (same as regular sensor alerts) |
| **TTL** | Optional inactivity timeout |

---

## Path Pattern Matching

The path pattern uses **glob-style wildcards** to match sensor full paths.

| Pattern | Matches |
|---|---|
| `MyProduct/Services/*/cpu` | Any sensor named `cpu` one level under `Services` |
| `MyProduct/**/memory` | Any `memory` sensor anywhere under `MyProduct` |
| `*/process/cpu` | Any product, then `process/cpu` |
| `MyProduct/worker-*/status` | Sensors like `worker-1/status`, `worker-prod/status` |

The match is against the **full sensor path** including the product name.

---

## Sensor Type Filter

A template can be restricted to a specific sensor type (Bool, Int, Double, etc.) or set to **Any type** to match all sensors at a given path regardless of type.

| Setting | Behaviour |
|---|---|
| Specific type (e.g. `Double`) | Only applies to Double sensors matching the path |
| **Any type** | Applies to all sensor types at matching paths |

---

## How Templates Are Applied

When a sensor is created or its path changes, HSM checks all templates in the sensor's folder:

1. Does the sensor's full path match the template's path pattern?
2. Does the sensor's type match the template's sensor type (or is it "Any type")?
3. Is the sensor's product in the template's folder?

If all three match, the template's policies are applied to the sensor.

> Templates are applied automatically. You do not need to manually assign them to sensors.

---

## Template vs Manual Policies

A sensor can have both template-applied policies and manually added policies. They coexist independently.

If a template is updated, the changes propagate automatically to all matching sensors.

---

## Example Use Cases

**Alert on high CPU for all services:**
```
Path pattern: */services/*/cpu
Sensor type: Double
Condition: Value > 90
Template: "[$product] $sensor CPU high: $value%"
```

**TTL alert for all heartbeat sensors:**
```
Path pattern: **/alive
Sensor type: Bool
TTL: 5 minutes
Message: "[$product] $path is not responding"
```

**Error status on any sensor in a critical product:**
```
Path pattern: CriticalProduct/**
Sensor type: Any type
Condition: Status = Error
Message: "🔴 [$product] $sensor: $status — $comment"
```
