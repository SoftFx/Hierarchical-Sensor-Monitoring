using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class StoreInfo : BaseRequestModel
    {
        public BaseValue BaseValue { get; init; }


        public StoreInfo(string key, string path) : base(key, path) { }
    }
}