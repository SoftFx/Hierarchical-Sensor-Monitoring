using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class StoreInfo : BaseRequestModel
    {
        public BaseValue BaseValue { get; init; }


        public void Deconstruct(out string key, out string path, out BaseValue baseValue)
        {
            key = Key;
            path = Path;
            baseValue = BaseValue;
        }
    }
}