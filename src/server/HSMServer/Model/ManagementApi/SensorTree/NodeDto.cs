using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Minimal reference to a tree node (product or folder): enough for an agent to
    /// address the node without fetching its full body.
    /// </summary>
    [Description("Minimal reference to a tree node (product or folder).")]
    public sealed record NodeRefDto
    {
        /// <summary>Node id.</summary>
        [Description("Node id.")]
        public Guid Id { get; init; }

        /// <summary>Node display name.</summary>
        [Description("Node display name.")]
        public string Name { get; init; }
    }


    /// <summary>
    /// Minimal reference to a sensor among a node's direct children.
    /// </summary>
    [Description("Minimal reference to a sensor among a node's direct children.")]
    public sealed record SensorRefDto
    {
        /// <summary>Sensor id.</summary>
        [Description("Sensor id.")]
        public Guid Id { get; init; }

        /// <summary>Sensor display name (the last segment of its path).</summary>
        [Description("Sensor display name (the last segment of its path).")]
        public string Name { get; init; }

        /// <summary>
        /// Sensor type. Value table (see the sensor endpoint for the full meaning):
        /// Boolean, Integer, Double, String, IntegerBar, DoubleBar, File, TimeSpan,
        /// Version, Rate, Enum.
        /// </summary>
        [Description("Sensor type. Value table: Boolean, Integer, Double, String, IntegerBar, DoubleBar, File, TimeSpan, Version, Rate, Enum.")]
        public string Type { get; init; }
    }


    /// <summary>
    /// A tree node — a root product or a nested folder — with its DIRECT children
    /// only (no recursion): folders and sensors are listed separately, both ordered
    /// by name and capped (the area's response bound); the totals tell the caller
    /// whether the full list was returned. Sensors of the whole subtree are served
    /// paginated by the sensor search endpoint with <c>product={node id}</c>.
    /// </summary>
    [Description("A tree node — a root product or a nested folder — with its DIRECT children only (no recursion). Sensors of the whole subtree are served paginated by the sensor search with product = this id.")]
    public sealed record NodeDto
    {
        /// <summary>Node id.</summary>
        [Description("Node id.")]
        public Guid Id { get; init; }

        /// <summary>Node display name.</summary>
        [Description("Node display name.")]
        public string Name { get; init; }

        /// <summary>Free-form node description (may be empty).</summary>
        [Description("Free-form node description (may be empty).")]
        public string Description { get; init; }

        /// <summary>
        /// Node kind — a root of the tree or a nested product ("folder" in the
        /// web UI's vocabulary; NOT the separate access-grouping Folder entity,
        /// which this surface does not address).
        /// Node kind table: product, folder.
        /// </summary>
        [Description("Node kind. Value table: product, folder (a nested product; NOT the separate access-grouping Folder entity).")]
        public string Type { get; init; }

        /// <summary>Full node path from its root product (the root product's own path is its name).</summary>
        [Description("Full node path from its root product (the root product's own path is its name).")]
        public string Path { get; init; }

        /// <summary>Node creation time (UTC).</summary>
        [Description("Node creation time (UTC).")]
        public DateTime CreationDate { get; init; }

        /// <summary>Parent node reference; null for a root product.</summary>
        [Description("Parent node reference; null for a root product.")]
        public NodeRefDto Parent { get; init; }

        /// <summary>
        /// Direct child folders, ordered by name, paginated (the only addressable
        /// surface for subfolder ids — walk the pages for a node with many of
        /// them; default page size is the 200 ceiling, so a single request covers
        /// every node below it).
        /// </summary>
        [Description("Direct child folders, ordered by name, paginated (200 per page by default — a single request covers every node).")]
        public List<NodeRefDto> Folders { get; init; } = [];

        /// <summary>Total number of direct child folders.</summary>
        [Description("Total number of direct child folders.")]
        public int TotalFolders { get; init; }

        /// <summary>1-based page of the folders list above (clamped into [1, totalPages]).</summary>
        [Description("1-based page of the folders list above (clamped into [1, totalPages]).")]
        public int FoldersPage { get; init; }

        /// <summary>Total page count of the folders list (0 when the node has no folders).</summary>
        [Description("Total page count of the folders list (0 when the node has no folders).")]
        public int FoldersTotalPages { get; init; }

        /// <summary>
        /// Direct child sensors, ordered by name, capped at the area's list
        /// ceiling (200); <c>totalSensors</c> carries the uncapped count — the
        /// paginated sensor search with product = this id serves the full list.
        /// </summary>
        [Description("Direct child sensors, ordered by name, capped at the area's list ceiling (200); `totalSensors` carries the uncapped count — the paginated sensor search with product = this id serves the full list.")]
        public List<SensorRefDto> Sensors { get; init; } = [];

        /// <summary>Total number of direct child sensors (the list above may be capped).</summary>
        [Description("Total number of direct child sensors (the list above may be capped).")]
        public int TotalSensors { get; init; }
    }
}
