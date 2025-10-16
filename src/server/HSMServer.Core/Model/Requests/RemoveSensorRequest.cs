using System;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record RemoveSensorRequest(Guid SensorId, InitiatorInfo InitiatoInfo = null, Guid? ParentId = null) : IUpdateRequest
    {
    }
}
