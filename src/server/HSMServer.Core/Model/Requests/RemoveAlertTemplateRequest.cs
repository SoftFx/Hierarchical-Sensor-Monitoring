using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal record RemoveAlertTemplateRequest(Guid TemplateId) : IUpdateRequest
    {
        public Guid Id { get; init; } = TemplateId;
    }
}
