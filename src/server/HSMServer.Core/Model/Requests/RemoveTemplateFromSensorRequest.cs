using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record RemoveTemplateFromSensorRequest(Guid SensorId, Guid TemplateId) : IUpdateRequest;
}
