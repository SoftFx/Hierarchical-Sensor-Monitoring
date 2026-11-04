using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.ComponentModel;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Journal;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.ManagementApi.Folders;
using HSMServer.Model.ManagementApi.SensorTree;

namespace HSMServer.Mcp
{
    // Tool result envelopes of the MCP surface (#1391). Item reads return the
    // REST DTOs verbatim (SensorDto, NodeDto, SensorHistoryDto, AlertTemplateDto,
    // AlertScheduleDto) — the same types and camelCase keys on both transports;
    // the list tools and the twelve write tools carry MCP-specific envelopes
    // (McpWriteResult below — the MCP-only price of one static output schema
    // per tool, #1605), and only find_sensors uses a compact item shape (the
    // full DTO embeds the current value, too heavy for a list).
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
    // Every record and property carries a [Description]; the tools opt into
    // structured content (UseStructuredContent on [McpServerTool], #1553), so
    // the descriptions reach agents through the tools/list OUTPUT schemas and
    // every tools/call success carries the structured JSON alongside the text
    // fallback block (clients that never read structuredContent lose nothing).

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

        /// <summary>Total page count at the effective limit (0 when the collection is empty).</summary>
        [Description("Total page count at the effective limit (0 when the collection is empty).")]
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
    /// Result of the list_sensor_policies tool — the id discovery half of the
    /// policy surface (#1615): without it the update/delete tools were
    /// one-shot (an id exists only inside the create response of the session
    /// that created it).
    /// </summary>
    [Description("Result of the list_sensor_policies tool: one page of the sensor's data policies plus the effective paging echo.")]
    public sealed record McpSensorPoliciesResult
    {
        /// <summary>The sensor's data policies of the served page, ordered by policy id; template-owned ones included, with their template linkage.</summary>
        [Description("The sensor's data policies of the served page, ordered by policy id; template-owned ones included, with their template linkage.")]
        public List<PolicyDto> Policies { get; init; }

        /// <summary>Total data policies on the sensor, whatever the limit.</summary>
        [Description("Total data policies on the sensor, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the sensor has no policies).</summary>
        [Description("Total page count at the effective limit (0 when the sensor has no policies).")]
        public int TotalPages { get; init; }
    }


    /// <summary>Result of the list_sensor_ttl_policies tool — the TTL twin of <see cref="McpSensorPoliciesResult"/> (#1615).</summary>
    [Description("Result of the list_sensor_ttl_policies tool: one page of the sensor's TTL policies plus the effective paging echo.")]
    public sealed record McpSensorTtlPoliciesResult
    {
        /// <summary>The sensor's TTL (inactivity) policies of the served page, ordered by policy id.</summary>
        [Description("The sensor's TTL (inactivity) policies of the served page, ordered by policy id.")]
        public List<TtlPolicyDto> TtlPolicies { get; init; }

        /// <summary>Total TTL policies on the sensor, whatever the limit.</summary>
        [Description("Total TTL policies on the sensor, whatever the limit.")]
        public int TotalFound { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The 1-based page actually served (a page past the end serves the last page).</summary>
        [Description("The 1-based page actually served (a page past the end serves the last page).")]
        public int Page { get; init; }

        /// <summary>Total page count at the effective limit (0 when the sensor has no TTL policies).</summary>
        [Description("Total page count at the effective limit (0 when the sensor has no TTL policies).")]
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


    /// <summary>
    /// Result envelope of the twelve journaled write tools (#1605): exactly
    /// one arm travels per call — `value` after an applied write (the stored
    /// resource, the same DTO the REST 200/201 body carries) or `preview`
    /// after a dry run (validation ran, nothing applied). The null arm is
    /// ABSENT on the wire (the SDK's WhenWritingNull, see the casing note
    /// above), so an agent discriminates the arms by the key that appears.
    ///
    /// The envelope exists so every write tool returns a CONCRETE closed
    /// type: the pre-#1605 object-typed unions exported as the permissive
    /// `true` schema, which the SDK's legacy-wire compatibility wrapped into
    /// `{"type":"object","properties":{"result":true},"required":["result"]}`
    /// — valid JSON Schema, but Claude Code accepts only object sub-schemas
    /// there and discards the whole tools/list (0 tools, silently
    /// "connected"). The REST arm keeps answering the bare DTO/preview: the
    /// envelope is the MCP-only price of one static output schema per tool.
    /// </summary>
    [Description("Result of a write tool: `value` carries the stored resource after an applied write, `preview` the affected-entities report after a dry run — exactly one of the two per call.")]
    public sealed record McpWriteResult<T>
    {
        /// <summary>The stored resource after an applied write; null (absent) after a dry run.</summary>
        [Description("The stored resource after an applied write — the same DTO the REST 200/201 body carries. Absent after a dry run.")]
        public T Value { get; init; }

        /// <summary>The dry-run preview (validation ran, nothing applied); null (absent) after an applied write.</summary>
        [Description("The dry-run preview: validation ran, nothing was applied — the kind/operation of the would-be write and every entity it would touch. Absent after an applied write.")]
        public WritePreviewDto Preview { get; init; }
    }


    /// <summary>Result of the list_journal_records tool.</summary>
    [Description("Result of the list_journal_records tool: one page of visible journal records plus the effective paging echo.")]
    public sealed record McpJournalRecordsResult
    {
        /// <summary>The visible journal records of the served slice, newest first.</summary>
        [Description("The visible journal records of the served slice, newest first.")]
        public List<JournalRecordDto> Records { get; init; }

        /// <summary>The effective page size (the clamped `limit`).</summary>
        [Description("The effective page size (the clamped `limit`).")]
        public int Limit { get; init; }

        /// <summary>The skip count actually served.</summary>
        [Description("The skip count actually served.")]
        public int Offset { get; init; }
    }
}
