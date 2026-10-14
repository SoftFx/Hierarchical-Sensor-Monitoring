using System;
using System.Collections.Generic;
using System.ComponentModel;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.Folders;
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
    //
    // Every record and property carries a [Description]: the MCP schema
    // exporter surfaces them into tools/list output schemas, which is how an
    // agent learns the envelope's paging semantics without external docs.

    /// <summary>Result of the list_products tool.</summary>
    [Description("Result of the list_products tool: one page of visible root products plus the effective paging echo.")]
    public sealed record McpProductsResult
    {
        /// <summary>The visible root products of the served page, ordered by name.</summary>
        [Description("The visible root products of the served page, ordered by name.")]
        public List<ProductDto> Products { get; init; }

        /// <summary>Total visible root products, whatever the limit.</summary>
        [Description("Total visible root products, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
        public int TotalPages { get; init; }
    }


    /// <summary>
    /// Compact sensor summary of the find_sensors tool — the fields an agent
    /// needs to pick sensors for analysis; the current value comes from
    /// get_sensor on the chosen id.
    /// </summary>
    [Description("Compact sensor summary of the find_sensors tool — the fields an agent needs to pick sensors for analysis; the current value comes from get_sensor on the chosen id.")]
    public sealed record McpSensorSummary
    {
        /// <summary>Sensor id.</summary>
        [Description("Sensor id — the id get_sensor and the policy tools take.")]
        public Guid Id { get; init; }

        /// <summary>Full sensor path from the root product.</summary>
        [Description("Full sensor path from the root product.")]
        public string Path { get; init; }

        /// <summary>Sensor type name (Double, IntegerBar, ...).</summary>
        [Description("Sensor type name. Value table: Boolean, Integer, Double, String, IntegerBar, DoubleBar, File, TimeSpan, Version, Rate, Enum.")]
        public string Type { get; init; }

        /// <summary>Current status (Ok, Error, OffTime); null when no data yet.</summary>
        [Description("Current status. Value table: Ok, Error, OffTime; null when no data yet.")]
        public string Status { get; init; }

        /// <summary>Human-readable unit (e.g. "%", "ms"); null when none.</summary>
        [Description("Human-readable unit (e.g. \"%\", \"ms\"); null when none.")]
        public string Unit { get; init; }
    }


    /// <summary>Result of the find_sensors tool.</summary>
    [Description("Result of the find_sensors tool: one page of matching sensors plus the effective paging echo.")]
    public sealed record McpSensorsResult
    {
        /// <summary>The matching sensors of the served page, ordered by path.</summary>
        [Description("The matching sensors of the served page, ordered by path.")]
        public List<McpSensorSummary> Sensors { get; init; }

        /// <summary>Total matching sensors, whatever the limit.</summary>
        [Description("Total matching sensors, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when nothing matches).</summary>
        [Description("Total page count at the effective limit (0 when nothing matches).")]
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_alert_templates tool.</summary>
    [Description("Result of the list_alert_templates tool: one page of visible templates plus the effective paging echo.")]
    public sealed record McpAlertTemplatesResult
    {
        /// <summary>The visible templates of the served page, ordered by name.</summary>
        [Description("The visible templates of the served page, ordered by name.")]
        public List<AlertTemplateDto> Templates { get; init; }

        /// <summary>Total visible templates, whatever the limit.</summary>
        [Description("Total visible templates, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_alert_schedules tool.</summary>
    [Description("Result of the list_alert_schedules tool: one page of schedules plus the effective paging echo.")]
    public sealed record McpAlertSchedulesResult
    {
        /// <summary>The schedules of the served page, ordered by name; sensor paths
        /// are filtered to the token owner's sight.</summary>
        [Description("The schedules of the served page, ordered by name; sensor paths are filtered to the token owner's sight.")]
        public List<AlertScheduleDto> Schedules { get; init; }

        /// <summary>Total schedules, whatever the limit.</summary>
        [Description("Total schedules, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_chats tool.</summary>
    [Description("Result of the list_chats tool: one page of visible chats plus the effective paging echo.")]
    public sealed record McpChatsResult
    {
        /// <summary>The visible chats of the served page, ordered by name.</summary>
        [Description("The visible chats of the served page, ordered by name.")]
        public List<ChatDto> Chats { get; init; }

        /// <summary>Total visible chats, whatever the limit.</summary>
        [Description("Total visible chats, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_folders tool.</summary>
    [Description("Result of the list_folders tool: one page of visible folders plus the effective paging echo.")]
    public sealed record McpFoldersResult
    {
        /// <summary>The visible folders of the served page, ordered by name.</summary>
        [Description("The visible folders of the served page, ordered by name.")]
        public List<FolderDto> Folders { get; init; }

        /// <summary>Total visible folders, whatever the limit.</summary>
        [Description("Total visible folders, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
        public int TotalPages { get; init; }
    }


    /// <summary>
    /// Result of the delete tools — MCP has no 204, so a delete answers an
    /// explicit success record naming the removed id.
    /// </summary>
    [Description("Result of the delete tools — MCP has no 204, so a delete answers an explicit success record naming the removed id.")]
    public sealed record McpDeletedResult
    {
        /// <summary>Id of the deleted resource.</summary>
        [Description("Id of the deleted resource.")]
        public Guid Id { get; init; }
    }
}
