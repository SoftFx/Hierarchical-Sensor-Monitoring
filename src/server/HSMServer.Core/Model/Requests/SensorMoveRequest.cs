using HSMServer.Core.SensorsUpdatesQueue;
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
        /// <summary>A real sensor already lives at the destination path.</summary>
        Conflict,
        Failed,
    }


    public sealed record SensorMoveResult(SensorMoveOutcome Outcome, string Error = null)
    {
        public string OldPath { get; init; }

        public string NewPath { get; init; }

        public bool IsOk => Outcome is SensorMoveOutcome.Moved or SensorMoveOutcome.Unchanged;
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
