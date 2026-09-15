using System;
using System.Collections.Generic;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.SensorTree;

namespace HSMServer.Mcp
{
    // Tool result envelopes of the MCP surface (#1391). Item reads return the
    // REST DTOs verbatim (SensorDto, NodeDto, SensorHistoryDto, AlertTemplateDto,
    // AlertScheduleDto) — the same types and camelCase keys on both transports;
    // only the list tools carry MCP-specific envelopes, and only find_sensors
    // uses a compact item shape (the full DTO embeds the current value, too
    // heavy for a list).
    //
    // Wire casing is camelCase on both transports: REST through MVC's JSON
    // options, MCP through the SDK's McpJsonUtilities.DefaultOptions (Web
    // defaults). The DTOs carry no naming policy of their own;
    // HsmMcpServerRegistrationTests pins the rendering so an SDK default change
    // cannot diverge the two surfaces silently. One deliberate shape nuance the
    // same test pins: the SDK options carry WhenWritingNull, MVC's do not — a
    // null DTO member (lastValue, unit, status, ...) is ABSENT on MCP and
    // written as null on REST, semantically identical to JSON consumers.
    //
    // Every list envelope echoes the EFFECTIVE paging (`limit`, the `page`
    // actually served, `totalPages`) — the paging the server applies after
    // normalization and clamping, mirroring the REST envelope's page/pageSize/
    // totalPages (#1392 review, round 5). Without the echo an agent cannot
    // derive a usable page: a `limit` it passes is silently rewritten (0 → 20,
    // >200 → 200), and `totalFound` alone invites dividing by the REQUESTED
    // limit — pages that skip or repeat items with no observable mismatch
    // (over-paging clamps to the last page instead of answering empty).

    /// <summary>Result of the list_products tool.</summary>
    public sealed record McpProductsResult
    {
        /// <summary>The visible root products of the served page, ordered by name.</summary>
        public List<ProductDto> Products { get; init; }

        /// <summary>Total visible root products, whatever the limit.</summary>
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        public int TotalPages { get; init; }
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
        /// <summary>The matching sensors of the served page, ordered by path.</summary>
        public List<McpSensorSummary> Sensors { get; init; }

        /// <summary>Total matching sensors, whatever the limit.</summary>
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when nothing matches).</summary>
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_alert_templates tool.</summary>
    public sealed record McpAlertTemplatesResult
    {
        /// <summary>The visible templates of the served page, ordered by name.</summary>
        public List<AlertTemplateDto> Templates { get; init; }

        /// <summary>Total visible templates, whatever the limit.</summary>
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_alert_schedules tool.</summary>
    public sealed record McpAlertSchedulesResult
    {
        /// <summary>The schedules of the served page, ordered by name; sensor paths
        /// are filtered to the token owner's sight.</summary>
        public List<AlertScheduleDto> Schedules { get; init; }

        /// <summary>Total schedules, whatever the limit.</summary>
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        public int TotalPages { get; init; }
    }
}
