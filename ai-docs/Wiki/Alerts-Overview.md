# Alerts Overview

Alerts are the core feature of HSM. You define rules (policies) that describe when and how to get notified about sensor state changes, inactivity, or value thresholds.

---

## Key Concepts

| Concept | Description |
|---|---|
| **Policy** | A single alert rule: if [condition] then [notify] |
| **Condition** | What triggers the alert: value threshold, status change, inactivity |
| **Destination** | Where to send the alert: a Telegram chat |
| **Template** | The notification message text, with dynamic variables |
| **Schedule** | When alerts can fire: working hours, repeat intervals |
| **Confirmation Period** | How long the condition must persist before alerting |
| **TTL** | Alert when a sensor stops sending data |

---

## How Policies Work

Each sensor can have one or more alert policies. When a new sensor value arrives, HSM evaluates all active policies against it:

```
New sensor value
    ↓
Check AlertSchedule (working hours) → skip if outside hours
    ↓
Evaluate all conditions (AND / OR)
    ↓
Wait for Confirmation Period (if set)
    ↓
Send notification to destination chats
```

Policies are evaluated independently. Multiple policies can trigger at the same time.

---

## Conditions

Each policy has one or more conditions combined with **AND** or **OR** logic.

### Properties

The **property** defines what part of the sensor value is checked:

| Property | Applies to | Description |
|---|---|---|
| `Value` | All scalar sensors | The current sensor value |
| `EmaValue` | Scalar sensors | Exponential Moving Average of the value |
| `Status` | All sensors | Current sensor status (Ok / Error / OffTime) |
| `Comment` | All sensors | The comment attached to the value |
| `Min` / `Max` / `Mean` | Bar sensors | Aggregate stats over the bar period |
| `Count` | Bar sensors | Number of values in the bar |
| `FirstValue` / `LastValue` | Bar sensors | First/last value in the bar period |
| `EmaMin` / `EmaMax` / `EmaMean` | Bar sensors | EMA of aggregate stats |
| `Length` | String sensors | String length |

### Operations

| Operation | Symbol | Applicable to |
|---|---|---|
| Less than | `<` | Numbers, TimeSpan, Version |
| Less than or equal | `<=` | Numbers, TimeSpan, Version |
| Greater than | `>` | Numbers, TimeSpan, Version |
| Greater than or equal | `>=` | Numbers, TimeSpan, Version |
| Equal | `=` | Numbers, strings, TimeSpan, Version |
| Not equal | `≠` | Numbers, strings, TimeSpan, Version |
| Contains | — | Strings |
| Starts with | — | Strings |
| Ends with | — | Strings |
| Is changed | — | Status, strings |
| Changed to Error | — | Status |
| Changed to Ok | — | Status |
| Is Error | — | Status |
| Is Ok | — | Status |

### Target

The **target** is the value the property is compared against. It can be:
- **Constant** — a fixed value you specify (e.g., `> 90`)
- **Last Value** — the previous sensor value (e.g., "alert if value changed")

---

## Message Templates

The alert message is a text template with variables that are filled in at the time the alert fires.

### Available Variables

| Variable | Description |
|---|---|
| `$product` | Parent product name |
| `$path` | Full sensor path |
| `$sensor` | Sensor name |
| `$status` | Current status (with icon) |
| `$prevStatus` | Previous status |
| `$value` | Current sensor value |
| `$prevValue` | Previous sensor value |
| `$time` | Time the value was received |
| `$comment` | Comment attached to the value |
| `$prevComment` | Previous comment |
| `$unit` | Sensor unit of measurement |
| `$min` / `$max` / `$mean` | Bar sensor stats |
| `$count` | Bar sensor value count |
| `$firstValue` / `$lastValue` | Bar sensor first/last values |
| `$emaValue` | EMA of current value |
| `$emaMin` / `$emaMax` / `$emaMean` | EMA of bar stats |
| `$property` | The alert property name |
| `$operation` | The alert operation symbol |
| `$target` | The comparison target |

### Example Templates

```
[$product] $sensor is $status: $value $unit
```
```
⚠️ $path — CPU at $value% (was $prevValue%)
```
```
[$product] $sensor hasn't recovered. Status: $status since $time
```

---

## Destinations

Each policy defines where to send notifications.

**Modes:**
- **From parent** — use the default Telegram chats configured on the parent product/folder. Additional specific chats can be added on top.
- **Custom** — send only to the explicitly listed chats.
- **All chats** — send to all Telegram chats registered in HSM.
- **Empty** — no notification (the policy only changes sensor status).

See [Telegram Setup](Telegram-Setup) for how to connect Telegram chats.

---

## Scheduling

### Simple Repeat

Controls how often an alert re-fires while the condition remains active:

| Mode | Description |
|---|---|
| Immediately | Fire once, do not repeat |
| Every 5 / 10 / 15 / 30 minutes | Repeat on interval |
| Hourly | Repeat every hour |
| Daily | Repeat once a day |
| Weekly | Repeat once a week |

**Instant Send** — when enabled, the first notification fires immediately when the condition triggers, regardless of the repeat interval.

### Working Hours (Alert Schedule)

For more complex suppression, you can assign an **Alert Schedule** to a policy. This lets you restrict alerts to specific days and time windows.

Schedules are managed separately under **Alerts → Schedules** and can be reused across multiple policies.

See [Alert Schedules](Alert-Schedules) for the full schedule configuration reference.

---

## Confirmation Period

By default, an alert fires as soon as the condition is met. With a **Confirmation Period**, the condition must remain true for a specified duration before the alert is sent.

**Use case:** A CPU sensor briefly spikes to 100% for 2 seconds. Without confirmation, you'd get a noisy alert. With a 1-minute confirmation period, only sustained spikes trigger alerts.

---

## TTL Alerts

A **TTL (Time-To-Live)** alert fires when a sensor has not sent any data within the configured interval.

**How it works:**
- You set a TTL duration on the sensor (e.g., 5 minutes)
- If no value arrives within that window, the TTL policy triggers
- When data resumes, an **Ok** recovery notification is sent automatically
- You can configure repeat notifications (every hour, daily, etc.) for prolonged silence
- The notification message includes a retry counter (e.g., `#1`, `#2`) showing how many times the alert has repeated

---

## Policy Inheritance

Policies can be defined at different levels of the hierarchy and inherited downward:

```
Product
  └── Folder
        └── Sensor
```

- **TTL policy** — inherited from parent product unless overridden at sensor level
- **Destination chats** — sensors inherit the default chats of their parent product; individual policies can add extra chats on top
- **Alert Templates** — apply policies automatically to all sensors matching a path pattern and sensor type (see [Alert Templates](Alert-Templates))

---

## Sensor Status

Every sensor has a status that reflects the outcome of policy evaluation:

| Status | Icon | Meaning |
|---|---|---|
| **Ok** | 🟢 | No policies triggered, or recovered after error |
| **Error** | 🔴 | At least one policy condition is active |
| **OffTime** | 🕑 | TTL expired — sensor has not sent data |

Status priority (ascending): `OffTime → Ok → Error`

---

## Export and Import

Alert policies for a folder can be exported to JSON and imported into another folder or server. This is useful for:
- Backing up alert configurations
- Copying a set of alerts to a new product
- Sharing alert rule templates across teams

Go to **Folder settings → Alerts → Export / Import**.

---

## See Also

- [Alert Conditions Reference](Alert-Conditions-Reference) — Complete reference of conditions for each sensor type
- [Alert Schedules](Alert-Schedules) — Configure working hours for alert delivery
- [Alert Templates](Alert-Templates) — Apply policies automatically to multiple sensors
- [Alerts Constructor](Alerts-constructor) — Build and manage alert policies
- [Telegram Setup](Telegram-Setup) — Configure Telegram bot for alert notifications
- [Sensor Types](Sensor-types) — Reference for all supported sensor types
