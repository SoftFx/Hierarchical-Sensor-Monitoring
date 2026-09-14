using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Minimal reference to a tree node (product or folder): enough for an agent to
    /// address the node without fetching its full body.
    /// </summary>
    public sealed record NodeRefDto
    {
        /// <summary>Node id.</summary>
        public Guid Id { get; init; }

        /// <summary>Node display name.</summary>
        public string Name { get; init; }
    }


    /// <summary>
    /// Minimal reference to a sensor among a node's direct children.
    /// </summary>
    public sealed record SensorRefDto
    {
        /// <summary>Sensor id.</summary>
        public Guid Id { get; init; }

        /// <summary>Sensor display name (the last segment of its path).</summary>
        public string Name { get; init; }

        /// <summary>
        /// Sensor type. Value table (see the sensor endpoint for the full meaning):
        /// Boolean, Integer, Double, String, IntegerBar, DoubleBar, File, TimeSpan,
        /// Version, Rate, Enum.
        /// </summary>
        public string Type { get; init; }
    }


    /// <summary>
    /// A tree node — a root product or a nested folder — with its DIRECT children
    /// only (no recursion): folders and sensors are listed separately, both ordered
    /// by name and capped (the area's response bound); the totals tell the caller
    /// whether the full list was returned. Sensors of the whole subtree are served
    /// paginated by the sensor search endpoint with <c>product={node id}</c>.
    /// </summary>
    public sealed record NodeDto
    {
        /// <summary>Node id.</summary>
        public Guid Id { get; init; }

        /// <summary>Node display name.</summary>
        public string Name { get; init; }

        /// <summary>Free-form node description (may be empty).</summary>
        public string Description { get; init; }

        /// <summary>
        /// Node kind — a root of the tree or a nested folder.
        /// Node kind table: product, folder.
        /// </summary>
        public string Type { get; init; }

        /// <summary>Full node path from its root product (the root product's own path is its name).</summary>
        public string Path { get; init; }

        /// <summary>Node creation time (UTC).</summary>
        public DateTime CreationDate { get; init; }

        /// <summary>Parent node reference; null for a root product.</summary>
        public NodeRefDto Parent { get; init; }

        /// <summary>
        /// Direct child folders, ordered by name, paginated (the only addressable
        /// surface for subfolder ids — walk the pages for a node with many of
        /// them; default page size is the 200 ceiling, so a single request covers
        /// every node below it).
        /// </summary>
        public List<NodeRefDto> Folders { get; init; } = [];

        /// <summary>Total number of direct child folders.</summary>
        public int TotalFolders { get; init; }

        /// <summary>1-based page of the folders list above (clamped into [1, totalPages]).</summary>
        public int FoldersPage { get; init; }

        /// <summary>Total page count of the folders list (0 when the node has no folders).</summary>
        public int FoldersTotalPages { get; init; }

        /// <summary>
        /// Direct child sensors, ordered by name, capped at the area's list
        /// ceiling (200); <c>totalSensors</c> carries the uncapped count — the
        /// paginated sensor search with product = this id serves the full list.
        /// </summary>
        public List<SensorRefDto> Sensors { get; init; } = [];

        /// <summary>Total number of direct child sensors (the list above may be capped).</summary>
        public int TotalSensors { get; init; }
    }
}
