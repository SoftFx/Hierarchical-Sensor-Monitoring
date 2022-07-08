using HSMServer.Core.Model;

namespace HSMServer.Core.Cache
{
    public readonly struct StoreInfo
    {
        public readonly string Path { get; init; }
        public readonly string Key { get; init; }
        public readonly BaseValue BaseValue { get; init; }


        public void Deconstruct(out string key, out string path, out BaseValue baseValue)
        {
            key = Key;
            path = Path;
            baseValue = BaseValue;
        }
    }
}