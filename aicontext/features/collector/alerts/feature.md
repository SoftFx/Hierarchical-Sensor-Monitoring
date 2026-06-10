# Feature: Alert DSL (collector side)

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - fluent builders that produce alert templates sent with sensor registration

---

## Overview

Sensors can carry alert definitions in their options (`Alerts` for instant, `BarAlerts` for bars, `TtlAlerts` for TTL). The DSL builds `AlertUpdateRequest` payloads embedded into `AddOrUpdateSensorRequest` at sensor registration; the **server** evaluates them. Wire shapes/enum values: `../../api/wire-contract/feature.md`.

## Builder surface (`Alerts/AlertsFactory.cs`)

Instant conditions: `IfValue<T>(op, target)`, `IfComment(op, target)`, `IfStatus(op)`, `IfLength(op, n)` (strings), `IfFileSize(op, n)` (files), `IfReceivedNewValue()`, `IfEmaValue(op, target)` (requires `Statistics = EMA`).

Bar conditions: `IfMin/IfMax/IfMean/IfCount/IfFirstValue/IfLastValue(op, value)`, `IfBarComment/IfBarStatus(op)`, `IfReceivedNewBarValue()`, EMA variants `IfEmaMin/IfEmaMax/IfEmaMean/IfEmaCount`.

Chaining & actions:

- `.And*` — additional conditions (And-combination);
- `.ThenSendNotification(template)` — notification text template (supports `$value`, `$comment`, etc. server-side placeholders);
- `.ThenSetIcon(icon)` — string or `AlertIcon`;
- `.ThenSetSensorError()` — escalate sensor status;
- `.AndConfirmationPeriod(TimeSpan)` — debounce before firing;
- schedule modifiers: `AndScheduledNotificationTime(...)`, repeat mode, instant-send flag;
- `.Build()` / `.BuildAndDisable()` → `InstantAlertTemplate` / `BarAlertTemplate` / `SpecialAlertTemplate`.

TTL alerts attach via `SensorOptions.TtlAlerts` (built from `AlertsFactory.IfInactivityPeriodIs(...)`-style special conditions); `DefaultAlertsOptions` flags (`DisableTtl = 1`, `DisableStatusChange = 2`) suppress server-side default alerts.

## Invariants

- The DSL is build-time only: changing a built template after registration has no effect until the sensor re-registers (`IsForceUpdate` controls whether collector-side settings overwrite user-modified server settings).
- Condition/property/operation combinations must match the sensor type (bar properties only on bar sensors, `Length` only on strings, `OriginalSize` only on files) — the server rejects mismatches.

## Key Files

| File | Purpose |
|---|---|
| `Alerts/AlertsFactory.cs` | Entry points |
| `Alerts/AlertConditions/*.cs` | Instant/bar/special condition builders |
| `Alerts/AlertActions/*.cs` | Action builders |
| `Alerts/AlertTemplates/*.cs` | Built template shapes → API conversion |

## Dependencies

- Used by: `sensors/` options, `default-sensors/` prototypes (default alerts), `public-api/`.
- Wire contract: `../../api/wire-contract/feature.md`.
