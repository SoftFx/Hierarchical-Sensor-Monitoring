using HSMServer.Core.Model;

namespace HSMServer.Core.Cache.UpdateEntities
{
    public sealed class ProductUpdate
    {
        public string Id { get; init; }

        public TimeIntervalModel ExpectedUpdateInterval { get; init; }
    }
}
