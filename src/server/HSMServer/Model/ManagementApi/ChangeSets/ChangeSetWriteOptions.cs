using System;
using System.Collections.Generic;
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
    public sealed record WritePreviewDto
    {
        public bool DryRun { get; init; }

        /// <summary>camelCase entry kind, e.g. "sensorDataPolicies", "alertTemplate".</summary>
        public string Kind { get; init; }

        /// <summary>camelCase operation: "create", "update" or "delete".</summary>
        public string Operation { get; init; }

        public string EntityId { get; init; }

        /// <summary>Display path of the primary entity at preview time.</summary>
        public string Path { get; init; }

        public List<PreviewAffectedEntityDto> Affected { get; init; }
    }


    public sealed record PreviewAffectedEntityDto
    {
        public string Id { get; init; }

        public string Path { get; init; }

        /// <summary>What the write would do there, e.g. "apply", "prune", "replace".</summary>
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
        /// header is a strict boolean: only "true" (any casing) previews, so
        /// a "0" or "no" never silently suppresses the write.
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
