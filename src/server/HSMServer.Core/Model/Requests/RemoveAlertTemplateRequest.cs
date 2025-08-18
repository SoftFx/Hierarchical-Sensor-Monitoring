using System;


namespace HSMServer.Core.Model.Requests
{
    internal class RemoveAlertTemplateRequest : BaseRequestModel
    {

        public Guid SensorId { get; init; }

        public RemoveAlertTemplateRequest(Guid key, string path) : base(key, path)
        {
        }
    }
}
