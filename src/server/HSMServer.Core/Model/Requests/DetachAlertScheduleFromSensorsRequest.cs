using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // One request per CHUNK of a root product branch's matching sensors: a
    // single queue pass detaches every listed sensor instead of one
    // round-trip per sensor (#1409). Per-sensor failures that the queue
    // handler swallows (TryUpdateSensor reports them instead of throwing)
    // are appended to Errors ON the queue thread and read by the dispatcher
    // AFTER the chunk's round-trip completes — that completion edge orders
    // the two accesses, and the bag folds into the detach's completion
    // TaskResult.
    internal sealed record DetachAlertScheduleFromSensorsRequest(
        IReadOnlyList<Guid> SensorIds,
        Guid ScheduleId,
        InitiatorInfo Initiator,
        ConcurrentBag<string> Errors = null) : IUpdateRequest;
}
