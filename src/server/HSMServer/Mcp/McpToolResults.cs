using System;
using System.Collections.Generic;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.SensorTree;

namespace HSMServer.Mcp
{
    // Tool result envelopes of the MCP surface (#1391). Item reads return the
    // REST DTOs verbatim (SensorDto, NodeDto, SensorHistoryDto, AlertTemplateDto,
    // AlertScheduleDto) — the same JSON shape on both transports; only the list
    // tools carry MCP-specific envelopes, and only find_sensors uses a compact
    // item shape (the full DTO embeds the current value, too heavy for a list).
    //
    // Wire casing is camelCase on both transports: REST through MVC's JSON
    // options, MCP through the SDK's protocol serializer.

    /// <summary>Result of the list_products tool.</summary>
    public sealed record McpProductsResult
    {
        /// <summary>The first `limit` visible root products, ordered by name.</summary>
        public List<ProductDto> Products { get; init; }

        /// <summary>Total visible root products, whatever the limit.</summary>
        public int TotalFound { get; init; }
    }


    /// <summary>
    /// Compact sensor summary of the find_sensors tool — the fields an agent
    /// needs to pick sensors for analysis; the current value comes from
    /// get_sensor on the chosen id.
    /// </summary>
    public sealed record McpSensorSummary
    {
        public Guid Id { get; init; }

        /// <summary>Full sensor path from the root product.</summary>
        public string Path { get; init; }

        /// <summary>Sensor type name (Double, IntegerBar, ...).</summary>
        public string Type { get; init; }

        /// <summary>Current status (Ok, Error, OffTime); null when no data yet.</summary>
        public string Status { get; init; }

        /// <summary>Human-readable unit (e.g. "%", "ms"); null when none.</summary>
        public string Unit { get; init; }
    }


    /// <summary>Result of the find_sensors tool.</summary>
    public sealed record McpSensorsResult
    {
        /// <summary>The first `limit` matching sensors, ordered by path.</summary>
        public List<McpSensorSummary> Sensors { get; init; }

        /// <summary>Total matching sensors, whatever the limit.</summary>
        public int TotalFound { get; init; }
    }


    /// <summary>Result of the list_alert_templates tool.</summary>
    public sealed record McpAlertTemplatesResult
    {
        /// <summary>The first `limit` visible templates, ordered by name.</summary>
        public List<AlertTemplateDto> Templates { get; init; }

        /// <summary>Total visible templates, whatever the limit.</summary>
        public int TotalFound { get; init; }
    }


    /// <summary>Result of the list_alert_schedules tool.</summary>
    public sealed record McpAlertSchedulesResult
    {
        /// <summary>The first `limit` schedules, ordered by name; sensor paths
        /// are filtered to the token owner's sight.</summary>
        public List<AlertScheduleDto> Schedules { get; init; }

        /// <summary>Total schedules, whatever the limit.</summary>
        public int TotalFound { get; init; }
    }
}
