using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    /// <summary>
    /// Wire contract of the /api/v1/alertSchedules read-only surface. The four durable
    /// fields are exactly what the web-UI editor shows.
    /// </summary>
    [Description("An alert schedule (working-time window): name, timezone, and the YAML schedule text, plus the sensor paths using it.")]
    public sealed record AlertScheduleDto
    {
        // #1570: the change set the write was journaled under — set on
        // journaled write echoes (REST header and MCP structured content),
        // absent on reads.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? ChangeSetId { get; init; }

        /// <summary>Schedule id.</summary>
        [Description("Schedule id.")]
        public Guid Id { get; init; }

        /// <summary>Schedule name.</summary>
        [Description("Schedule name (unique per server).")]
        public string Name { get; init; }

        /// <summary>IANA/Windows timezone id, as configured by the operator.</summary>
        [Description("IANA/Windows timezone id, as configured by the operator.")]
        public string Timezone { get; init; }

        /// <summary>
        /// The YAML schedule text — the editor's source of truth; day schedules and
        /// overrides are parsed from it, never stored separately.
        /// </summary>
        [Description(AlertScheduleYamlDocs.Format)]
        public string Schedule { get; init; }

        /// <summary>
        /// Full paths of the sensors currently using the schedule — FILTERED to the
        /// products the token's owner can see, so a token never learns paths outside
        /// its owner's sight.
        /// </summary>
        [Description("Full paths of the sensors currently using the schedule — filtered to the products the token's owner can see.")]
        public List<string> Sensors { get; init; } = [];
    }
}
