# Feature: Alert DSL (collector side)

> Owner: collector | Last reviewed: 2026-06-17 | Canonical: yes
> Scope: Collector - fluent builders that produce alert templates sent with sensor registration

---

## Overview

Sensors can carry alert definitions in their options (`Alerts` for instant, `BarAlerts` for bars, `TtlAlerts` for TTL). The DSL builds `AlertUpdateRequest` payloads embedded into `AddOrUpdateSensorRequest` at sensor registration; the **server** evaluates them. Wire shapes/enum values: `../../api/wire-contract/feature.md`.

## Builder surface (`Alerts/AlertsFactory.cs`)

Instant conditions: `IfValue<T>(op, target)`, `IfComment(op, target)`, `IfStatus(op)`, `IfLenght(op, n)` (strings — **note the actual exported name is misspelled** "Lenght"; chaining variant is correctly `AndLength`), `IfFileSize(op, n)` (files), `IfReceivedNewValue()`, `IfEmaValue(op, target)` (requires `Statistics = EMA`).

Bar conditions: `IfMin/IfMax/IfMean/IfCount/IfFirstValue/IfLastValue(op, value)`, `IfBarComment/IfBarStatus(op)`, `IfReceivedNewBarValue()`, EMA variants `IfEmaMin/IfEmaMax/IfEmaMean/IfEmaCount`.

TTL: `AlertsFactory.IfInactivityPeriodIs(TimeSpan? = null)` → `SpecialAlertCondition` — the only entry point for TTL alert templates; its `TtlValue` feeds `TTLs` on the wire.

Chaining & actions:

- `.And*` — additional conditions (And-combination);
- `.ThenSendNotification(template, AlertDestinationMode destination = FromParent)` — notification text template (supports `$value`, `$comment`, etc. server-side placeholders); destination controls chat routing;
- `.ThenSendScheduledNotification(template, DateTime time, AlertRepeatMode, bool instantSend, AlertDestinationMode = FromParent)` / chaining `.AndSendScheduledNotification(...)`;
- `.ThenSetIcon(icon)` — string or `AlertIcon { Ok, Warning, Error, Pause, ArrowUp, ArrowDown, Clock, Hourglass }`; the enum maps to UTF-8 emoji via `IconExtensions.ToUtf8`, and the **string** is what goes on the wire;
- `.ThenSetSensorError()` — escalate sensor status;
- `.AndConfirmationPeriod(TimeSpan)` — debounce before firing;
- `.Build()` / `.BuildAndDisable()` → `InstantAlertTemplate` / `BarAlertTemplate` / `SpecialAlertTemplate`.

TTL alerts attach via `SensorOptions.TtlAlerts` (or the singular `TtlAlert` convenience setter); `DefaultAlertsOptions` flags (`DisableTtl = 1`, `DisableStatusChange = 2`) suppress server-side default alerts.

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

## Native port (C++) — #1098

The native collector (`src/native/collector`) ports the alert **model and registration serialization**, not the C# fluent-sugar layer. A C ABI builder (`hsm_collector_create_alert` + `hsm_alert_add_condition` / `hsm_alert_set_notification` / `hsm_alert_set_scheduled_notification` / `hsm_alert_set_icon` / `hsm_alert_set_sensor_error` / `hsm_alert_set_confirmation_period` / `hsm_alert_set_disabled` / `hsm_alert_set_inactivity_period`) builds an `AlertData`, and `hsm_sensor_attach_alert` folds it into the sensor's registration before Start.

- **Explicit, not sugared**: the ABI takes the frozen numeric `property/operation/combination/target/destination` enums directly (the contract level). C#'s `IfValue`/`IfLenght`/etc. are convenience wrappers that pick those values; they are C#-only and out of native scope.
- **AlertIcon → emoji**: `hsm_alert_set_icon` maps the eight `AlertIcon` values to the exact UTF-8 emoji `IconExtensions.ToUtf8` produces; the wire serializer escapes them to `\uXXXX` like System.Text.Json (e.g. Warning → `⚠`).
- **One serializer, two consumers**: `BuildAlertJson` renders an `AlertUpdateRequest` byte-identically to STJ and is embedded by BOTH the internal corpus registration text and the wire registration, so they never drift. A TTL alert lands in `TtlAlerts` and its inactivity drives `TTLs` (ticks), mirroring `ApiConverters`.
- **Coverage**: byte parity is pinned by the paired golden unit tests — C# `WireFormatGoldenLockTests.Registration_with_alerts_matches_the_native_golden_bytes` and native `NativeWireRegistrationWithAlertsMatchesNetByteLayout` assert the SAME literal. The cross-language live path (DSL→registration on C#, ABI→registration on native) is pinned by `alert_registration_contract.hsmtest`.
- **Deferred**: scheduled-notification time formatting beyond ISO, the `IfLenght` misspelling alias decision, and EMA `Statistics` gating live with the server-side evaluation and the public builder API (#1100); native only emits the registration payload.

## Dependencies

- Used by: `sensors/` options, `default-sensors/` prototypes (default alerts), `public-api/`.
- Wire contract: `../../api/wire-contract/feature.md`.
