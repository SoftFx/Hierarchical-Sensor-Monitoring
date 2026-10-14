using System;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// A root product visible to the token's owner. Nested folders are not listed
    /// here — walk them through the node endpoint or search sensors recursively.
    /// </summary>
    [Description("A root product visible to the token's owner. Nested folders are not listed here — walk them through the node endpoint or search sensors recursively.")]
    public sealed record ProductDto
    {
        /// <summary>Product id.</summary>
        [Description("Product id (a root of the sensor tree).")]
        public Guid Id { get; init; }

        /// <summary>Product display name (the first segment of every sensor path under it).</summary>
        [Description("Product display name (the first segment of every sensor path under it).")]
        public string Name { get; init; }

        /// <summary>Free-form product description (may be empty).</summary>
        [Description("Free-form product description (may be empty).")]
        public string Description { get; init; }

        /// <summary>Product creation time (UTC).</summary>
        [Description("Product creation time (UTC).")]
        public DateTime CreationDate { get; init; }
    }
}
