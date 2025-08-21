using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;
using System;


namespace HSMServer.Core.Model.Requests
{
    internal class RemoveSensorRequest : IUpdateRequest
    {
        public Guid SensorId { get; init; }

        public InitiatorInfo InitiatorInfo { get; init; }

        public Guid? ParentId { get; init; }

        public RemoveSensorRequest(Guid sensorId)
        {
            SensorId = sensorId;
        }
    }
}
