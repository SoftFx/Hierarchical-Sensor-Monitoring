using HSMServer.Core.Cache.UpdateEntities;

namespace HSMServer.Core.Model.Requests
{
    public sealed class SensorAddOrUpdateRequestModel : BaseRequestModel
    {
        public SensorUpdate Update { get; set; }

        public SensorType Type { get; set; }


        public SensorAddOrUpdateRequestModel(string key, string path) : base(key, path) { }
    }
}
