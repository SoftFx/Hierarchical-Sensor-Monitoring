using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Requests
{
    public sealed class SensorUpdateRequestModel : BaseRequestModel
    {
        public SensorUpdate Update { get; init; }

        public SensorType Type { get; init; }


        public SensorUpdateRequestModel(BaseRequestModel request) : base(request.Key, request.Path) { }
    }
}
