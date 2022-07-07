using HSMServer.Core.Model;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public readonly struct StoreInfo
    {
        public readonly string Path { get; init; }
        public readonly string Key { get; init; }
        public readonly BaseValue BaseValue { get; init; }
    }
}