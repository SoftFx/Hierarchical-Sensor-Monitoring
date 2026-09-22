using System;
using System.Collections.Generic;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // One request per ROOT product branch: a single queue pass detaches every
    // listed sensor instead of one round-trip per sensor (#1409).
    internal sealed record DetachAlertScheduleFromSensorsRequest(IReadOnlyList<Guid> SensorIds, Guid ScheduleId, InitiatorInfo Initiator) : IUpdateRequest;
}
