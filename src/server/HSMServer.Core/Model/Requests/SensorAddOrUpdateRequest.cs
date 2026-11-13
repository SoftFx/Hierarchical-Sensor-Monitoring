using System;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;


namespace HSMServer.Core.Model.Requests
{
    public sealed record SensorAddOrUpdateRequest : BaseUpdateRequest
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }

        // False when the client sent no type and Type is the Boolean default:
        // such a registration must not displace a moved sensor's alias (#1718).
        public bool TypeIsExplicit { get; init; }


        public SensorAddOrUpdateRequest(Guid productId, string path) : base(productId, path)
        {
        }
    }
}
