using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Requests
{
    public sealed class SensorAddOrUpdateRequestModel : BaseRequestModel
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }


        public SensorAddOrUpdateRequestModel(BaseRequestModel request) : base(request.Key, request.Path) { }
    }
}
