using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    // Wire contract of the /api/v1/alertSchedules read-only surface (#1352, epic #1347).
    // The four durable fields are exactly what the web-UI editor shows; Sensors carries
    // the full paths of the sensors currently using the schedule (the UI list page shows
    // them as a tooltip) — FILTERED to the boundaries the caller can see, so a
    // folder-scoped token never learns paths outside its grants.
    public sealed record AlertScheduleDto
    {
        public Guid Id { get; init; }

        public string Name { get; init; }

        // IANA/Windows timezone id, as configured by the operator.
        public string Timezone { get; init; }

        // The YAML schedule text — the editor's source of truth; day schedules and
        // overrides are parsed from it, never stored separately.
        public string Schedule { get; init; }

        public List<string> Sensors { get; init; } = [];
    }
}
