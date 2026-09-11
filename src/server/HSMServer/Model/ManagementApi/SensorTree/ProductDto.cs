using System;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// A root product visible to the token's owner. Nested folders are not listed
    /// here — walk them through the node endpoint or search sensors recursively.
    /// </summary>
    public sealed record ProductDto
    {
        /// <summary>Product id.</summary>
        public Guid Id { get; init; }

        /// <summary>Product display name (the first segment of every sensor path under it).</summary>
        public string Name { get; init; }

        /// <summary>Free-form product description (may be empty).</summary>
        public string Description { get; init; }

        /// <summary>Product creation time (UTC).</summary>
        public DateTime CreationDate { get; init; }
    }
}
