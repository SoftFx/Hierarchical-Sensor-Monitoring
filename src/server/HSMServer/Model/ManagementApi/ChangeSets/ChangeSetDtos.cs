using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    // === Read models of the change-set surface (#1570), shared by REST,
    // MCP and the web UI. Policy images render as the serialized entity row
    // (a stable, complete diff view); ids and times render in wire types.

    public sealed record ChangeSetSummaryDto
    {
        public string Id { get; init; }

        public string Author { get; init; }

        public string AuthorType { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        /// <summary>applied | rolledBack | partial</summary>
        public string Status { get; init; }

        public int EntryCount { get; init; }

        public int RestoredEntryCount { get; init; }

        /// <summary>Display paths of the touched entities, visible to the caller.</summary>
        public List<string> Entities { get; init; }
    }


    public sealed record ChangeSetDetailsDto
    {
        public ChangeSetSummaryDto Summary { get; init; }

        public List<ChangeSetEntryDto> Entries { get; init; }
    }


    public sealed record ChangeSetEntryDto
    {
        public int Sequence { get; init; }

        public string Kind { get; init; }

        public string Operation { get; init; }

        public string EntityId { get; init; }

        public string Path { get; init; }

        public bool Restored { get; init; }

        public string RestoredBy { get; init; }

        public List<string> Before { get; init; }

        public List<string> After { get; init; }

        public string BeforeTemplate { get; init; }

        public string AfterTemplate { get; init; }

        public string BeforeSchedule { get; init; }

        public string AfterSchedule { get; init; }

        public List<NodeImageDto> Fanout { get; init; }
    }


    public sealed record NodeImageDto
    {
        public string NodeKind { get; init; }

        public string Id { get; init; }

        public string Path { get; init; }

        public List<string> BeforePolicies { get; init; }

        public List<string> AfterPolicies { get; init; }

        public List<string> BeforeTtlPolicies { get; init; }

        public List<string> AfterTtlPolicies { get; init; }
    }


    public sealed record ChangeSetFilter
    {
        public string Author { get; init; }

        public DateTime? FromUtc { get; init; }

        public DateTime? ToUtc { get; init; }

        /// <summary>applied | rolledBack | partial (case-insensitive).</summary>
        public string Status { get; init; }

        /// <summary>Only change sets touching this entity id.</summary>
        public Guid? EntityId { get; init; }

        public int Limit { get; init; } = 50;

        public int Offset { get; init; }
    }


    // === Rollback ===

    public sealed record RollbackResultDto
    {
        public string ChangeSetId { get; init; }

        public bool DryRun { get; init; }

        public bool Forced { get; init; }

        /// <summary>completed | partial | conflicts</summary>
        public string Status { get; init; }

        public string Message { get; init; }

        /// <summary>The change set the rollback itself was journaled under (redo handle).</summary>
        public string RollbackChangeSetId { get; init; }

        public List<RollbackEntryReportDto> Entries { get; init; }
    }


    public sealed record RollbackEntryReportDto
    {
        public int Sequence { get; init; }

        public string Kind { get; init; }

        public string Operation { get; init; }

        public string Path { get; init; }

        /// <summary>wouldRestore | restored | skipped | failed</summary>
        public string Status { get; init; }

        public string Message { get; init; }

        public List<string> Conflicts { get; init; }
    }
}
