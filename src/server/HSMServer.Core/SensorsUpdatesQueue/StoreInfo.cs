using HSMServer.Core.Model;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public readonly struct StoreInfo
    {
        public string Path { get; init; }

        public string Key { get; init; }

        public BaseValue BaseValue { get; init; }


        public void Deconstruct(out string key, out string path, out BaseValue baseValue)
        {
            key = Key;
            path = Path;
            baseValue = BaseValue;
        }
    }
}