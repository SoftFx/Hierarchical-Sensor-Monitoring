using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    /// <summary>
    /// Per-request write options of the journaled management-API surface
    /// (#1570): the client-supplied change-set grouping id and the dry-run
    /// flag. Transport-agnostic — REST carries them as headers, MCP tools as
    /// optional arguments, and both transports pass this record down to the
    /// administration services.
    /// </summary>
    public sealed record ChangeSetWriteOptions
    {
        public Guid? ChangeSetId { get; init; }

        public bool DryRun { get; init; }
    }


    /// <summary>
    /// The affected-entities report of a dry-run write (#1570): validation
    /// ran, nothing was applied. For a template write the report names every
    /// sensor the fan-out would touch (apply or prune); for policy and
    /// schedule writes it names the resolved target nodes.
    /// </summary>
    public sealed record WritePreviewDto
    {
        /// <summary>The change-set id the write would be journaled under.</summary>
        public string ChangeSetId { get; init; }

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
        public static WritePreviewDto Create(ChangeSetWriteOptions write, string kind, string operation,
            Guid entityId, string path, IEnumerable<PreviewAffectedEntityDto> affected) =>
            new()
            {
                ChangeSetId = ChangeSetRecorder.ResolveChangeSetId(write).ToString(),
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
    /// The REST transport of the write options (#1570): headers in, the
    /// journaled change-set id echoed on every successful write response.
    /// </summary>
    public static class ChangeSetHeaders
    {
        public const string ChangeSetId = "X-HSM-Change-Set";

        public const string DryRun = "X-HSM-Dry-Run";

        /// <summary>
        /// Parse the write options of a management-API request. An absent or
        /// malformed change-set id answers null options (the write stands
        /// alone) — the header is advisory grouping, never a 400.
        /// </summary>
        public static ChangeSetWriteOptions WriteOptions(this Microsoft.AspNetCore.Http.HttpRequest request)
        {
            var changeSetId = Guid.TryParse(request.Headers[ChangeSetId], out var parsed) ? parsed : (Guid?)null;
            var dryRun = request.Headers[DryRun].Count > 0 &&
                         !string.Equals(request.Headers[DryRun], "false", StringComparison.OrdinalIgnoreCase);

            return changeSetId is null && !dryRun ? null : new ChangeSetWriteOptions
            {
                ChangeSetId = changeSetId,
                DryRun = dryRun,
            };
        }

        /// <summary>The change-set id echo on write responses (201/200/204).</summary>
        public static void SetChangeSet(this Microsoft.AspNetCore.Http.HttpResponse response, Guid? changeSetId)
        {
            if (changeSetId is { } id)
                response.Headers[ChangeSetId] = id.ToString();
        }
    }
}
