using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// One enum option of an Enum sensor: the option registered by the collector
    /// that maps the raw int to a label.
    /// </summary>
    public sealed record SensorEnumOptionDto
    {
        /// <summary>Raw enum int as sent by the collector.</summary>
        public int Value { get; init; }

        /// <summary>Option label (the collector's option name).</summary>
        public string Label { get; init; }

        /// <summary>Option description (may be empty).</summary>
        public string Description { get; init; }
    }


    /// <summary>
    /// Sensor metadata plus its current value. The shape is IDENTICAL in the search
    /// list and in the single-sensor endpoint — an agent never needs a second fetch
    /// to see what a found sensor currently reads.
    /// </summary>
    public sealed record SensorDto
    {
        /// <summary>Sensor id.</summary>
        public Guid Id { get; init; }

        /// <summary>Full sensor path from the root product (e.g. "ProductX/Host/Network/eth0").</summary>
        public string Path { get; init; }

        /// <summary>Sensor display name (the last segment of the path).</summary>
        public string Name { get; init; }

        /// <summary>Free-form sensor description (may be empty) — the main search target.</summary>
        public string Description { get; init; }

        /// <summary>
        /// Sensor type. Value table: Boolean, Integer, Double, String, IntegerBar,
        /// DoubleBar, File, TimeSpan, Version, Rate, Enum.
        /// </summary>
        public string Type { get; init; }

        /// <summary>
        /// Human-readable unit of the value (e.g. "%", "ms", "Bytes/sec", "# per sec");
        /// null when the sensor has none.
        /// </summary>
        public string Unit { get; init; }

        /// <summary>
        /// Current sensor status (policy-aware; a muted sensor reports OffTime).
        /// Value table: 0=Ok, 1=Error, 255=OffTime. Null when the sensor has no data yet.
        /// </summary>
        public string Status { get; init; }

        /// <summary>
        /// Sensor lifecycle state. Value table: Available, Muted, Blocked.
        /// </summary>
        public string State { get; init; }

        /// <summary>Sensor creation time (UTC).</summary>
        public DateTime CreationDate { get; init; }

        /// <summary>Time of the newest value (UTC); null when the sensor has no data yet.</summary>
        public DateTime? LastUpdate { get; init; }

        /// <summary>Reference to the sensor's root product.</summary>
        public NodeRefDto Product { get; init; }

        /// <summary>Reference to the sensor's immediate parent node (folder or root product).</summary>
        public NodeRefDto Parent { get; init; }

        /// <summary>Current value (see <c>SensorValueDto</c>); null when the sensor has no data yet.</summary>
        public SensorValueDto LastValue { get; init; }

        /// <summary>Registered enum options; present on Enum sensors only, null otherwise.</summary>
        public List<SensorEnumOptionDto> EnumOptions { get; init; }
    }
}
