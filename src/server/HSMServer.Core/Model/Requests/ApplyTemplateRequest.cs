using System;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record ApplyTemplateRequest(Guid SensorId, AlertTemplateModel Template) : IUpdateRequest
    {
        public AlertTemplateModel AlertTemplateModel { get; } = Template;
    }
}
