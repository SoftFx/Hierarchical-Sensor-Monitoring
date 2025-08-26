using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record AddAlertTemplateRequest(AlertTemplateModel Model) : IUpdateRequest
    {
        public AlertTemplateModel AlertTemplateModel { get; } = Model;
    }
}
