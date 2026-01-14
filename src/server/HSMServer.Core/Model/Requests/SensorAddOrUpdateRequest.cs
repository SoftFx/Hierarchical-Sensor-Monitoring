using System;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;


namespace HSMServer.Core.Model.Requests
{
    public sealed record SensorAddOrUpdateRequest : BaseUpdateRequest
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }


        public SensorAddOrUpdateRequest(Guid productId, string path) : base(productId, path)
        {
        }
    }
}
