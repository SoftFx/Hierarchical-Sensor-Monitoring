using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    /// <summary>
    /// Wire contract of the /api/v1/alertSchedules read-only surface. The four durable
    /// fields are exactly what the web-UI editor shows.
    /// </summary>
    public sealed record AlertScheduleDto
    {
        /// <summary>Schedule id.</summary>
        public Guid Id { get; init; }

        /// <summary>Schedule name.</summary>
        public string Name { get; init; }

        /// <summary>IANA/Windows timezone id, as configured by the operator.</summary>
        public string Timezone { get; init; }

        /// <summary>
        /// The YAML schedule text — the editor's source of truth; day schedules and
        /// overrides are parsed from it, never stored separately.
        /// </summary>
        public string Schedule { get; init; }

        /// <summary>
        /// Full paths of the sensors currently using the schedule — FILTERED to the
        /// boundaries the caller can see, so a folder-scoped token never learns paths
        /// outside its grants.
        /// </summary>
        public List<string> Sensors { get; init; } = [];
    }
}
