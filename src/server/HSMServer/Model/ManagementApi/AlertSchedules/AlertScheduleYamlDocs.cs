namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    // The agent-facing contract documentation of the schedule YAML dialect — the
    // single source both schedule DTOs reference from their [Description]s, so the
    // input schema (create/update) and the output schema (reads) cannot drift.
    // The text states exactly what AlertScheduleParser enforces; keep it in sync
    // with the parser when a rule changes.
    internal static class AlertScheduleYamlDocs
    {
        internal const string Format =
            @"The working-time schedule as YAML text (camelCase keys). Structure and rules:
- daySchedules (required, non-empty): each entry has:
  - id: optional string; auto-generated when omitted. Referenced by overrides.customScheduleDates[].scheduleType.
  - days (required, non-empty): day names mon, tue, wed, thu, fri, sat, sun (3-letter or full English).
  - windows (required, non-empty): entries of start/end as HH:mm (00:00..24:00); start < end, each window at least 1 minute, windows within one entry must not overlap.
- disabledDates (optional): 'yyyy-MM-dd' dates (years 2000..2100) with no working time at all.
- overrides (optional):
  - enabledDates: 'yyyy-MM-dd' dates that follow their weekday schedule despite disabledDates.
  - customScheduleDates: per-date entries with EITHER scheduleType (the id of a daySchedules entry, max 50 chars) OR their own windows.
A date must not appear in both enabledDates and customScheduleDates; custom dates must be unique.
Example:
daySchedules:
  - id: workdays
    days: [mon, tue, wed, thu, fri]
    windows:
      - start: 09:00
        end: 18:00
  - days: [sat]
    windows:
      - start: 10:00
        end: 14:00
disabledDates: ['2026-11-03']
overrides:
  enabledDates: ['2026-12-25']
  customScheduleDates:
    - date: '2027-01-08'
      scheduleType: workdays";
    }
}
