using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    /// <summary>
    /// Per-request write options of the journaled configuration surface
    /// (#1588): the dry-run flag and the request's undo-group id. The group
    /// id is SERVER-generated — one HTTP request (or MCP tool call, or web
    /// form post) journals under exactly one id, so every write of a single
    /// request rolls back together. Clients never supply or see group ids
    /// anymore; the rollback surface addresses them through journal records.
    /// </summary>
    public sealed record ChangeSetWriteOptions
    {
        public Guid? ChangeSetId { get; init; }

        public bool DryRun { get; init; }
    }


    /// <summary>
    /// The affected-entities report of a dry-run write: validation ran,
    /// nothing was applied. For a template write the report names every
    /// sensor the fan-out would touch (apply or prune); for policy and
    /// schedule writes it names the resolved target nodes.
    /// </summary>
    // #1605: the preview records ride the MCP write tools' OUTPUT schemas
    // (inside the McpWriteResult envelope), so they carry [Description]s on
    // every member like every other MCP-bound DTO (#1553).
    // Open for the deletion preview (#1637): a sensor deletion's dry run
    // carries deletion-specific members (history count/size, mute state)
    // alongside the shared affected-entities report — the derived record
    // rides the same envelope without growing every journaled write's shape.
    public record WritePreviewDto
    {
        /// <summary>Always true — this is a preview, nothing was applied.</summary>
        [Description("Always true — this is a preview, nothing was applied.")]
        public bool DryRun { get; init; }

        /// <summary>camelCase entry kind, e.g. "sensorDataPolicies", "alertTemplate".</summary>
        [Description("camelCase entry kind, e.g. \"sensorDataPolicies\", \"alertTemplate\".")]
        public string Kind { get; init; }

        /// <summary>camelCase operation: "create", "update" or "delete".</summary>
        [Description("camelCase operation of the would-be write: \"create\", \"update\" or \"delete\".")]
        public string Operation { get; init; }

        /// <summary>Id of the primary entity of the would-be write.</summary>
        [Description("Id of the primary entity of the would-be write.")]
        public string EntityId { get; init; }

        /// <summary>Display path of the primary entity at preview time.</summary>
        [Description("Display path of the primary entity at preview time.")]
        public string Path { get; init; }

        /// <summary>Every entity the write would touch, with what it would do there.</summary>
        [Description("Every entity the write would touch, with what it would do there.")]
        public List<PreviewAffectedEntityDto> Affected { get; init; }
    }


    public sealed record PreviewAffectedEntityDto
    {
        /// <summary>Id of the affected entity.</summary>
        [Description("Id of the affected entity.")]
        public string Id { get; init; }

        /// <summary>Display path of the affected entity.</summary>
        [Description("Display path of the affected entity.")]
        public string Path { get; init; }

        /// <summary>What the write would do there, e.g. "apply", "prune", "replace".</summary>
        [Description("What the write would do there, e.g. \"apply\", \"prune\", \"replace\".")]
        public string Change { get; init; }
    }


    /// <summary>
    /// Shared dry-run preview builder of the journaled write surface: one
    /// shape for policy, template and schedule writes across REST and MCP.
    /// </summary>
    public static class WritePreviews
    {
        public static WritePreviewDto Create(string kind, string operation,
            Guid entityId, string path, IEnumerable<PreviewAffectedEntityDto> affected) =>
            new()
            {
                DryRun = true,
                Kind = kind,
                Operation = operation,
                EntityId = entityId.ToString(),
                Path = path,
                Affected = [.. affected],
            };

        public static string CamelOf<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            var name = value.ToString();

            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }


    /// <summary>
    /// The REST transport of the write options: the dry-run header, and the
    /// per-request undo-group id minted once per HTTP request and shared by
    /// every journaled write that request performs (#1588: one request = one
    /// rollback group).
    /// </summary>
    public static class ChangeSetHeaders
    {
        public const string DryRun = "X-HSM-Dry-Run";

        private const string RequestGroupIdItem = "Hsm.UndoGroupId";

        /// <summary>
        /// The undo-group id of THIS request: minted on first use and cached
        /// in HttpContext.Items, so multi-write requests (a web form post, a
        /// batch endpoint) journal all their writes under one group. The web
        /// controllers resolve their write options through the same helper.
        /// </summary>
        public static Guid RequestGroupId(HttpContext context)
        {
            if (context.Items[RequestGroupIdItem] is Guid cached)
                return cached;

            var minted = Guid.NewGuid();

            context.Items[RequestGroupIdItem] = minted;

            return minted;
        }

        /// <summary>
        /// Parse the write options of a management-API request: the dry-run
        /// header plus the request's implicit undo-group id. The dry-run
        /// header is a strict boolean: "true" (any casing) previews, and a
        /// value that parses as neither true nor false never reaches this
        /// parse — <c>DryRunHeaderFilter</c> fails the request closed with a
        /// 400 before the write runs (#1675), so a typo cannot turn a
        /// preview into the real, irreversible write.
        /// </summary>
        public static ChangeSetWriteOptions WriteOptions(this HttpRequest request)
        {
            var dryRun = request.Headers[DryRun].Count > 0 &&
                         bool.TryParse(request.Headers[DryRun], out var dryRunParsed) && dryRunParsed;

            return new ChangeSetWriteOptions
            {
                ChangeSetId = RequestGroupId(request.HttpContext),
                DryRun = dryRun,
            };
        }
    }
}
