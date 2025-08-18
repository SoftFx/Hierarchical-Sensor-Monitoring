using System;


namespace HSMServer.Core.Model.Requests
{
    internal class ExpireSensorRequest : BaseRequestModel
    {
        public BaseSensorModel Sensor { get; init; }

        public bool IsTimeout { get; init; }

        public ExpireSensorRequest(Guid key, string path) : base(key, path)
        {
        }
    }
}
