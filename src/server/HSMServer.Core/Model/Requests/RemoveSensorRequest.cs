using System;
using System.Collections.Generic;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // #1570: PurgeIds is the batch-purge collector of a whole removal sweep
    // (the self-destroy pass). The queue machinery cannot thread call-site
    // state, so the REQUEST carries it: a non-null collector makes the
    // removal append the sensor id instead of purging the undo journal per
    // sensor (the sweep purges once for every removed id at its end).
    internal sealed record RemoveSensorRequest(Guid SensorId, InitiatorInfo InitiatoInfo = null, Guid? ParentId = null,
        List<Guid> PurgeIds = null) : IUpdateRequest
    {
    }
}
