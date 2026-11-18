using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The create-product request body: the new root product's name. Validation
    /// (required, length, charset, root-name uniqueness) is owned by the
    /// administration service, not by binding attributes — the REST and MCP
    /// transports share one validation decision with the web UI's error strings.
    /// </summary>
    public sealed class CreateProductRequestDto
    {
        /// <summary>The new product's name: 1..100 characters from
        /// '0-9a-zA-Z .,_-=#:;%&amp;*()', unique among root products. Surrounding
        /// whitespace is trimmed before validation and storage.</summary>
        [Description("Product name: 1..100 characters of '0-9a-zA-Z .,_-=#:;%&*()', unique among root products; surrounding whitespace is trimmed.")]
        public string Name { get; init; }
    }
}
