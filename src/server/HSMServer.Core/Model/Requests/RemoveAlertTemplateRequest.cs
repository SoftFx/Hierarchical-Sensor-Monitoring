using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal record RemoveAlertTemplateRequest(Guid TemplateId, Guid ProductId, bool IsPrimary = false) : IUpdateRequest
    {
        public Guid Id { get; init; } = TemplateId;

        public Guid ProductId { get; init; } = ProductId;
        public bool IsPrimary { get; init; } = IsPrimary;

    }
}
