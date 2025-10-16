using System;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record AddAlertTemplateRequest(AlertTemplateModel Model, Guid ProductId, bool IsPrimary = false) : IUpdateRequest
    {
        public AlertTemplateModel AlertTemplateModel { get; } = Model;

        public Guid ProductId { get; } = ProductId;

        public bool IsPrimary { get; } = IsPrimary;
    }
}
