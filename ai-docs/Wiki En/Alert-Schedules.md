# Alert Schedules

An Alert Schedule defines **working hours** for alert delivery. When a policy has a schedule assigned, alerts are only sent during the configured time windows — all other times, notifications are silently suppressed.

Schedules are reusable: one schedule can be assigned to multiple alert policies across different sensors.

---

## Managing Schedules

Go to **Alerts → Schedules** in the HSM Web UI to create, edit, or delete schedules.

Each schedule has:
- **Name** — displayed in policy settings
- **Timezone** — all time windows are evaluated in this timezone
- **YAML configuration** — the schedule body (see format below)

---

## YAML Format

The schedule is configured as YAML. The editor validates the YAML in real time and shows errors.

### Minimal example — weekdays only

```yaml
daySchedules:
  - days: [Mon, Tue, Wed, Thu, Fri]
    windows:
      - start: "09:00"
        end: "18:00"
```

### Full example with all options

```yaml
daySchedules:
  - days: [Mon, Tue, Wed, Thu, Fri]
    windows:
      - start: "09:00"
        end: "13:00"
      - start: "14:00"
        end: "18:00"
  - days: [Sat]
    windows:
      - start: "10:00"
        end: "14:00"

disabledDates:
  - "2025-12-25"
  - "2026-01-01"

overrides:
  enabledDates:
    - "2025-03-08"
  customScheduleDates:
    - date: "2026-04-01"
      windows:
        - start: "10:00"
          end: "12:00"
```

---

## Fields Reference

### `daySchedules` (required)

List of day-of-week schedules. Each entry covers one or more days with their time windows.

```yaml
daySchedules:
  - days: [Mon, Wed, Fri]   # days this entry applies to
    windows:
      - start: "09:00"      # format: HH:mm (24-hour)
        end: "17:00"
```

**Day names:**

| Value | Day |
|---|---|
| `Mon` | Monday |
| `Tue` | Tuesday |
| `Wed` | Wednesday |
| `Thu` | Thursday |
| `Fri` | Friday |
| `Sat` | Saturday |
| `Sun` | Sunday |

**Rules:**
- At least one entry with at least one day and one window is required
- Time format is `HH:mm` (24-hour, e.g. `09:00`, `23:30`)
- `start` must be earlier than `end`
- Minimum window duration: 1 minute
- Windows within a single day entry must not overlap
- Multiple windows per day are allowed (e.g. for lunch breaks)

---

### `disabledDates` (optional)

List of dates on which alerts are **never sent**, even if they fall on a normally active day.

```yaml
disabledDates:
  - "2025-12-25"
  - "2026-01-01"
  - "2026-05-09"
```

Date format: `yyyy-MM-dd`

---

### `overrides` (optional)

Exceptions to the regular schedule.

#### `enabledDates`

Dates that are treated as a **regular working day** even if they fall on a day not in `daySchedules` (e.g., a Saturday that is a makeup workday).

```yaml
overrides:
  enabledDates:
    - "2025-11-08"   # Saturday worked instead of a holiday
```

The time windows used are those of the matching `daySchedules` entry for that day of week.

#### `customScheduleDates`

Dates with a **completely custom time window**, overriding both regular and disabled rules.

```yaml
overrides:
  customScheduleDates:
    - date: "2026-04-01"
      windows:
        - start: "10:00"
          end: "14:00"
```

Each `customScheduleDate` must have either `windows` or `scheduleType` (not both).

> A date cannot appear in both `enabledDates` and `customScheduleDates` at the same time.

---

## Decision Priority

When checking if an alert should fire at a given time, HSM evaluates in this order:

1. **customScheduleDates** — if the date has a custom override, use those windows (or scheduleType)
2. **enabledDates** — if the date is force-enabled, use the regular daySchedule windows for that weekday
3. **disabledDates** — if the date is disabled, suppress the alert
4. **daySchedules** — use the regular schedule for that weekday

If none of the above match the current day, the alert is suppressed.

---

## Timezone

The `Timezone` field is set in the HSM UI (not in the YAML body). It uses standard IANA timezone IDs:

```
Europe/Minsk
Europe/Moscow
UTC
America/New_York
```

All time windows in the YAML are interpreted in this timezone. HSM stores and compares times in UTC internally and converts to local time for schedule evaluation.

---

## Validation Rules

The YAML editor enforces these rules before saving:

- `daySchedules` must be present and non-empty
- Each day schedule must have at least one day and one window
- Time format must be `HH:mm`
- `start` < `end`, minimum window is 1 minute
- No overlapping windows within the same day entry
- No duplicate dates in `customScheduleDates`
- No date in both `enabledDates` and `customScheduleDates`
- Dates must be in range 2000–2100

---

## Assigning a Schedule to a Policy

In the alert policy editor, click **Schedule** and select an existing Alert Schedule from the list. The policy will then only fire within the configured working hours.

This is separate from the **Repeat** setting (which controls how often to re-notify while the condition is active). Both can be active at the same time.
