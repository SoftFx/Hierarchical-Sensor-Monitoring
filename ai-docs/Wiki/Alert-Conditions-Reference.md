# Alert Conditions Reference

This page is a complete reference of which conditions are available for each sensor type.

---

## Sensor Types Overview

| Type | Value kind | Bar? |
|---|---|---|
| `Bool` | true / false | No |
| `Int` | integer number | No |
| `Double` | floating point number | No |
| `String` | text | No |
| `TimeSpan` | duration | No |
| `Version` | version number (e.g. 1.2.3) | No |
| `Rate` | floating point, shown as rate/speed | No |
| `Enum` | integer mapped to named values | No |
| `IntegerBar` | aggregated int over time window | Yes |
| `DoubleBar` | aggregated double over time window | Yes |
| `File` | file content | No |

---

## Conditions by Sensor Type

### Bool

| Property | Operations | Target |
|---|---|---|
| `Value` | `=`, `≠` | `true` / `false` |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### Int

| Property | Operations | Target |
|---|---|---|
| `Value` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant or last value |
| `EmaValue` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### Double

| Property | Operations | Target |
|---|---|---|
| `Value` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant or last value |
| `EmaValue` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### String

| Property | Operations | Target |
|---|---|---|
| `Value` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text or last value |
| `Length` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### TimeSpan

| Property | Operations | Target |
|---|---|---|
| `Value` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant duration or last value |
| `EmaValue` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### Version

| Property | Operations | Target |
|---|---|---|
| `Value` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant version or last value |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

Versions are compared component by component: `1.2.10 > 1.2.9`.

---

### Rate

Same as **Double** — the value is a floating-point number formatted as a rate.

---

### Enum

| Property | Operations | Target |
|---|---|---|
| `Value` | `=`, `≠` | constant integer (enum ordinal) |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### IntegerBar / DoubleBar

Bar sensors aggregate multiple values over a time window (default: 5 minutes) and produce Min, Max, Mean, Count, First, and Last statistics.

| Property | Operations | Target |
|---|---|---|
| `Min` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant or last value |
| `Max` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant or last value |
| `Mean` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant or last value |
| `Count` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `FirstValue` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `LastValue` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `EmaMin` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `EmaMax` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `EmaMean` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `EmaCount` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

### File

| Property | Operations | Target |
|---|---|---|
| `OriginalSize` | `<`, `<=`, `>`, `>=`, `=`, `≠` | constant (bytes) |
| `Status` | `IsOk`, `IsError`, `IsChanged`, `IsChangedToOk`, `IsChangedToError` | — |
| `Comment` | `=`, `≠`, `Contains`, `StartsWith`, `EndsWith`, `IsChanged` | text |

---

## Condition Combination

Multiple conditions in one policy are combined with **AND** or **OR**:

- **AND** — all conditions must be true for the alert to fire
- **OR** — any single condition being true fires the alert

---

## Target Types

The **target** is the value the property is compared against:

| Target type | Description |
|---|---|
| **Constant** | A fixed value you enter (e.g. `> 90.0`) |
| **Last value** | The previous value of the same property (e.g. "alert if value changed by any amount") |

`Last value` target is useful for detecting any change without specifying a threshold.

---

## EMA (Exponential Moving Average)

EMA properties smooth out short-term spikes by giving more weight to recent values. Use them to alert on sustained trends rather than momentary fluctuations.

- `EmaValue` — EMA of the scalar value
- `EmaMin`, `EmaMax`, `EmaMean` — EMA of bar aggregates

EMA is calculated server-side as new values arrive.

---

## Status Operations

All sensor types support status-based conditions:

| Operation | Triggers when |
|---|---|
| `IsOk` | Current status is Ok |
| `IsError` | Current status is Error |
| `IsChanged` | Status changed from the previous value (any direction) |
| `IsChangedToOk` | Status changed from Error/OffTime to Ok |
| `IsChangedToError` | Status changed from Ok/OffTime to Error |

Status conditions are useful for **recovery notifications** (e.g., send an alert when an error clears).

---

## See Also

- [Alerts Overview](Alerts-Overview) — Introduction to the alert system
- [Alert Schedules](Alert-Schedules) — Configure working hours for alert delivery
- [Alert Templates](Alert-Templates) — Apply policies automatically to multiple sensors
- [Alerts Constructor](Alerts-constructor) — Build and manage alert policies
- [Sensor Types](Sensor-types) — Reference for all supported sensor types
