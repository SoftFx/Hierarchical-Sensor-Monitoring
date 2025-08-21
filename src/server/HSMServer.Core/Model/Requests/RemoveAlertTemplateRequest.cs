using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal class RemoveAlertTemplateRequest : IUpdateRequest
    {
        public Guid SensorId { get; init; }

        public Guid TemplateId { get; init; }

        public RemoveAlertTemplateRequest(Guid sensorId, Guid templateId)
        {
            SensorId = sensorId;
            TemplateId = templateId;
        }
    }
}
