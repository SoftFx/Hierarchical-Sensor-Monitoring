using HSMServer.Core.Cache.UpdateEntities;
using System;

namespace HSMServer.Core.Model.Requests
{
    public sealed record SensorAddOrUpdateRequest : BaseUpdateRequest
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }


        public SensorAddOrUpdateRequest(string productName, string path) : base(productName, path)
        {
        }
    }
}
