using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// One enum option of an Enum sensor: the option registered by the collector
    /// that maps the raw int to a label.
    /// </summary>
    [Description("One enum option of an Enum sensor: maps the raw int to a label.")]
    public sealed record SensorEnumOptionDto
    {
        /// <summary>Raw enum int as sent by the collector.</summary>
        [Description("Raw enum int as sent by the collector.")]
        public int Value { get; init; }

        /// <summary>Option label (the collector's option name).</summary>
        [Description("Option label (the collector's option name).")]
        public string Label { get; init; }

        /// <summary>Option description (may be empty).</summary>
        [Description("Option description (may be empty).")]
        public string Description { get; init; }
    }


    /// <summary>
    /// Sensor metadata plus its current value. The shape is IDENTICAL in the search
    /// list and in the single-sensor endpoint — an agent never needs a second fetch
    /// to see what a found sensor currently reads.
    /// </summary>
    [Description("Sensor metadata plus its current value.")]
    public sealed record SensorDto
    {
        /// <summary>Sensor id.</summary>
        [Description("Sensor id.")]
        public Guid Id { get; init; }

        /// <summary>Full sensor path from the root product (e.g. "ProductX/Host/Network/eth0").</summary>
        [Description("Full sensor path from the root product (e.g. \"ProductX/Host/Network/eth0\").")]
        public string Path { get; init; }

        /// <summary>Sensor display name (the last segment of the path).</summary>
        [Description("Sensor display name (the last segment of the path).")]
        public string Name { get; init; }

        /// <summary>Free-form sensor description (may be empty) — the main search target.</summary>
        [Description("Free-form sensor description (may be empty) — the main search target.")]
        public string Description { get; init; }

        /// <summary>
        /// Sensor type. Value table: Boolean, Integer, Double, String, IntegerBar,
        /// DoubleBar, File, TimeSpan, Version, Rate, Enum.
        /// </summary>
        [Description("Sensor type. Value table: Boolean, Integer, Double, String, IntegerBar, DoubleBar, File, TimeSpan, Version, Rate, Enum.")]
        public string Type { get; init; }

        /// <summary>
        /// Human-readable unit of the value (e.g. "%", "ms", "Bytes/sec", "# per sec");
        /// null when the sensor has none.
        /// </summary>
        [Description("Human-readable unit of the value (e.g. \"%\", \"ms\", \"Bytes/sec\", \"# per sec\"); null when the sensor has none.")]
        public string Unit { get; init; }

        /// <summary>
        /// Current sensor status (policy-aware; a muted sensor reports OffTime),
        /// as the enum NAME (the wire value is the string, not the number).
        /// Value table: Ok, Error, OffTime. Null when the sensor has no data yet.
        /// </summary>
        [Description("Current sensor status (policy-aware; a muted sensor reports OffTime), as the enum NAME. Value table: Ok, Error, OffTime. Null when the sensor has no data yet.")]
        public string Status { get; init; }

        /// <summary>
        /// Sensor lifecycle state. Value table: Available, Muted, Blocked.
        /// </summary>
        [Description("Sensor lifecycle state. Value table: Available, Muted, Blocked.")]
        public string State { get; init; }

        /// <summary>Sensor creation time (UTC).</summary>
        [Description("Sensor creation time (UTC).")]
        public DateTime CreationDate { get; init; }

        /// <summary>
        /// Server RECEIVE time of the newest value (UTC) — not the collector's
        /// measurement time (the value's own <c>time</c>); the two can differ for
        /// aggregated or re-sent values. Null when the sensor has no data yet.
        /// </summary>
        [Description("Server RECEIVE time of the newest value (UTC) — not the collector's measurement time (the value's own `time`); the two can differ for aggregated or re-sent values. Null when the sensor has no data yet.")]
        public DateTime? LastUpdate { get; init; }

        /// <summary>Reference to the sensor's root product.</summary>
        [Description("Reference to the sensor's root product.")]
        public NodeRefDto Product { get; init; }

        /// <summary>Reference to the sensor's immediate parent node (folder or root product).</summary>
        [Description("Reference to the sensor's immediate parent node (folder or root product).")]
        public NodeRefDto Parent { get; init; }

        /// <summary>Current value (see <c>SensorValueDto</c>); null when the sensor has no data yet.</summary>
        [Description("Current value; null when the sensor has no data yet.")]
        public SensorValueDto LastValue { get; init; }

        /// <summary>Registered enum options; present on Enum sensors only, null otherwise.</summary>
        [Description("Registered enum options; present on Enum sensors only, null otherwise.")]
        public List<SensorEnumOptionDto> EnumOptions { get; init; }
    }
}
