using System;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record DetachAlertScheduleFromSensorRequest(Guid SensorId, Guid ScheduleId, InitiatorInfo Initiator) : IUpdateRequest;
}
