using HSMServer.Core.SensorsUpdatesQueue;
using System.Collections.Generic;
using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.Core.Model.Requests
{
    public enum SensorMoveOutcome
    {
        /// <summary>The sensor now lives at the destination path.</summary>
        Moved,
        /// <summary>The destination is the sensor's current path; nothing changed.</summary>
        Unchanged,
        SensorNotFound,
        DestinationNotFound,
        InvalidPath,
        /// <summary>A real sensor already lives at the destination path (and no merge was asked, or the types differ).</summary>
        Conflict,
        Failed,
        /// <summary>The sensor moved onto an occupied path and absorbed the occupant's history (#1720).</summary>
        Merged,
    }


    /// <summary>
    /// What a move would do (#1721 dry run): the predicted outcome and its side
    /// effects, computed from the live model without changing anything.
    /// </summary>
    public sealed record SensorMovePreview(SensorMoveOutcome Outcome, string Error = null)
    {
        public string OldPath { get; init; }

        public string NewPath { get; init; }

        /// <summary>The sensor the move would merge (Merged) or collide with (Conflict).</summary>
        public Guid? OccupantId { get; init; }

        /// <summary>Destination nodes the move would create, as full paths.</summary>
        public List<string> CreatedNodes { get; init; } = [];

        /// <summary>Alert templates whose alerts the move would remove from the sensor.</summary>
        public List<(Guid Id, string Name)> TemplatesDetached { get; init; } = [];

        /// <summary>Alert templates whose alerts the move would add to the sensor.</summary>
        public List<(Guid Id, string Name)> TemplatesApplied { get; init; } = [];

        /// <summary>Effective settings that would change: (name, before, after).</summary>
        public List<(string Name, string Before, string After)> SettingsChanged { get; init; } = [];
    }


    /// <summary>What a move does when a real sensor already holds the destination path.</summary>
    public enum SensorMoveConflict
    {
        /// <summary>Refuse with <see cref="SensorMoveOutcome.Conflict"/>.</summary>
        Fail,
        /// <summary>
        /// The moved sensor survives with its id, settings and policies and takes over
        /// the occupant's history rows (its own row wins a same-tick collision); the
        /// occupant is removed. Sensors of different types never merge.
        /// </summary>
        Merge,
    }


    public sealed record SensorMoveResult(SensorMoveOutcome Outcome, string Error = null)
    {
        public string OldPath { get; init; }

        public string NewPath { get; init; }

        /// <summary>The absorbed sensor's id (Merged only).</summary>
        public Guid? MergedSensorId { get; init; }

        /// <summary>History rows copied from the absorbed sensor (Merged only).</summary>
        public long MergedValues { get; init; }

        /// <summary>Absorbed rows dropped because the moved sensor had a row at the same tick (Merged only).</summary>
        public long MergeCollisions { get; init; }

        public bool IsOk => Outcome is SensorMoveOutcome.Moved or SensorMoveOutcome.Unchanged or SensorMoveOutcome.Merged;
    }


    /// <summary>
    /// Move one sensor to <see cref="DestinationPath"/> under the root product
    /// <see cref="DestinationProductId"/> (#1718, ADR-0018). Runs on the queue of
    /// the sensor's current root product, recorded in <see cref="SourceRootId"/>.
    /// </summary>
    internal sealed record SensorMoveRequest(Guid SensorId, Guid SourceRootId, Guid DestinationProductId,
        string DestinationPath, InitiatorInfo Initiator) : IUpdateRequest
    {
        public SensorMoveResult Result { get; set; }
    }


    /// <summary>
    /// First step of a merge (#1720), on the queue that owns the occupant: copy its
    /// history under the survivor's id, turn its path and aliases into aliases of the
    /// survivor, remove it. The move that follows lands on a path that is then only
    /// the survivor's own alias.
    /// </summary>
    internal sealed record SensorAbsorbRequest(Guid SurvivorId, Guid OccupantId, Guid RootId, string Path, InitiatorInfo Initiator) : IUpdateRequest
    {
        public long Copied { get; set; }

        public long Collisions { get; set; }

        public string Error { get; set; }

        // The occupant is gone (its rows copied): from here on a failure must not
        // be reported as "nothing happened".
        public bool Absorbed { get; set; }

        // Set when step 1 found the types different (a re-check: the occupant
        // may have been replaced since the caller looked).
        public SensorMoveOutcome? Conflict { get; set; }

        // Set when the occupant no longer held the path: nothing to merge,
        // the move goes ahead as a plain one.
        public bool Vanished { get; set; }
    }


    /// <summary>Last step of a merge: refresh the survivor's cache on the queue that owns it.</summary>
    internal sealed record SensorMergeRefreshRequest(Guid SensorId, Guid ExpectedRootId, int Hops) : IUpdateRequest;


    /// <summary>
    /// A value that reached a sensor through an alias owned by another root
    /// product: added on the owning product's queue, keeping the sensor model
    /// single-writer (ADR-0018).
    /// </summary>
    internal sealed record AliasValueRequest(Guid SensorId, Guid ExpectedRootId, HSMCommon.Model.BaseValue Value, int Hops) : IUpdateRequest;


    /// <summary>
    /// Alias housekeeping on the owning sensor's queue: expire aliases past the
    /// sensor's SelfDestroy interval and persist recent hits, or remove one alias
    /// (<see cref="RemoveProductId"/>/<see cref="RemovePath"/>) when asked to.
    /// </summary>
    internal sealed record SensorAliasMaintenanceRequest(Guid SensorId, Guid ExpectedRootId, DateTime UtcNow) : IUpdateRequest
    {
        // Fire-and-forget senders let the handler follow a sensor that moved
        // to another queue; awaiting senders read Misrouted and retry.
        public bool Redispatch { get; init; }

        public int Hops { get; init; }

        public bool Misrouted { get; set; }

        // Persist the sensor's aliases even when nothing changed — after a
        // merge adopted aliases on another queue (#1720).
        public bool ForcePersist { get; init; }

        public Guid? RemoveProductId { get; init; }

        public string RemovePath { get; init; }

        public InitiatorInfo Initiator { get; init; }

        public bool Removed { get; set; }
    }


    /// <summary>A registration that reached a sensor through an alias of another root product.</summary>
    internal sealed record AliasRegistrationRequest(Cache.UpdateEntities.SensorUpdate Update, Guid ExpectedRootId, int Hops) : IUpdateRequest;


    /// <summary>
    /// Template re-evaluation of a sensor that moved into another root product:
    /// runs on the destination's queue, which owns the sensor from now on.
    /// </summary>
    internal sealed record SensorTemplateReevaluationRequest(Guid SensorId, Guid ExpectedRootId, int Hops) : IUpdateRequest;


    // Path validation and normalization shared with ingestion (BaseUpdateRequest).
    internal sealed record SensorPathCheck : BaseUpdateRequest
    {
        public SensorPathCheck(Guid productId, string path) : base(productId, path) { }
    }
}
