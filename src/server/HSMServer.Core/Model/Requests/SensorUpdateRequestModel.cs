using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Requests
{
    public sealed class SensorUpdateRequestModel : BaseRequestModel
    {
        public SensorUpdate Update { get; }


        public SensorUpdateRequestModel(BaseRequestModel request, SensorUpdate update)
            : base(request.Key, request.Path)
        {
            Update = update;
        }
    }
}
