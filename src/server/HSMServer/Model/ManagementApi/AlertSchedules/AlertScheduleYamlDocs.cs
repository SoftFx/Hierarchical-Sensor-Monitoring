namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    // The agent-facing contract documentation of the schedule YAML dialect — the
    // single source both schedule DTOs reference from their [Description]s, so the
    // input schema (create/update) and the read DTOs (Swagger/IDE today; tools/list
    // output schemas once #1553 emits them) cannot drift.
    // The text states exactly what AlertScheduleParser enforces; keep it in sync
    // with the parser when a rule changes.
    internal static class AlertScheduleYamlDocs
    {
        internal const string Format =
            @"The working-time schedule as YAML text (camelCase keys). Structure and rules:
- daySchedules (required key; an empty list validates but leaves no recurring working time): each entry has:
  - id: optional string; auto-generated when omitted (a fresh Guid on every parse, never echoed back — an entry must carry an explicit id to be referenced by scheduleType).
  - days (required, non-empty): day names mon, tue, wed, thu, fri, sat, sun (3-letter or full English).
  - windows (required, non-empty): entries of start/end as HH:mm (00:00..23:59); start < end, each window at least 1 minute, windows within one entry must not overlap. A window running to the end of the day writes end: 1.00:00 (TimeSpan with a days part; 24:00 is NOT accepted).
- disabledDates (optional): 'yyyy-MM-dd' dates (years 2000..2100) with no working time at all.
- overrides (optional):
  - enabledDates: 'yyyy-MM-dd' dates that follow their weekday schedule despite disabledDates.
  - customScheduleDates: per-date entries with EITHER scheduleType (resolves to the first daySchedules entry whose id equals it, else the first entry with a day whose English name starts with it (case-sensitive); max 50 chars; a value matching no id and no day name falls back to the date's normal handling: disabledDates first, then the regular weekday schedule) OR their own windows. A custom date with windows or a resolved scheduleType takes precedence over disabledDates.
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
