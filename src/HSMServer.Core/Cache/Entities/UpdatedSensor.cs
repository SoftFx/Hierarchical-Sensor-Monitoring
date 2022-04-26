using System;

namespace HSMServer.Core.Cache.Entities
{
    public class UpdatedSensor
    {
        public Guid Id { get; init; }

        public string Product { get; init; }

        public string Path { get; init; }

        public string Description { get; init; }

        public string ExpectedUpdateInterval { get; init; }

        public string Unit { get; init; }
    }
}
