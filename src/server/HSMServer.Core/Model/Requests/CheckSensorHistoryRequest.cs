using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record CheckSensorHistoryRequest(Guid SensorId) : IUpdateRequest
    {
        public Guid Id { get; } = SensorId;
    }
}
