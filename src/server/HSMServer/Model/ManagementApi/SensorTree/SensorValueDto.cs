using System;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// One sensor value — the current (<c>lastValue</c>) or a history point — as a
    /// uniform envelope: <c>value</c> is typed by the sensor type. Boolean, Integer,
    /// Double, Rate → a JSON number/boolean; String → string; TimeSpan → a .NET
    /// round-trip string ("d.hh:mm:ss"); Version → "major.minor.build.revision";
    /// Enum → <c>EnumValueDto</c>; IntegerBar/DoubleBar → <c>BarValueDto</c>;
    /// File → <c>FileValueDto</c> (metadata only — file content is never served).
    /// </summary>
    public sealed record SensorValueDto
    {
        /// <summary>Value time as recorded by the collector (UTC, ISO 8601).</summary>
        public DateTime Time { get; init; }

        /// <summary>
        /// Sensor status at this value. Value table: 0=Ok, 1=Error, 255=OffTime.
        /// </summary>
        public string Status { get; init; }

        /// <summary>Comment attached to the value (may be null).</summary>
        public string Comment { get; init; }

        /// <summary>The typed value — see the record summary for the per-type shapes.</summary>
        public object Value { get; init; }
    }


    /// <summary>Typed value of an Enum sensor: the raw int plus its resolved option label.</summary>
    public sealed record EnumValueDto
    {
        /// <summary>Raw enum int as sent by the collector.</summary>
        public int Value { get; init; }

        /// <summary>Option label from the sensor's enum options; null when the option is not registered.</summary>
        public string Label { get; init; }
    }


    /// <summary>Typed value of an IntegerBar/DoubleBar sensor: the statistics of one aggregation period.</summary>
    public sealed record BarValueDto
    {
        /// <summary>Minimum of the period.</summary>
        public double Min { get; init; }

        /// <summary>Maximum of the period.</summary>
        public double Max { get; init; }

        /// <summary>Mean of the period.</summary>
        public double Mean { get; init; }

        /// <summary>Number of aggregated values in the period.</summary>
        public int Count { get; init; }
    }


    /// <summary>
    /// Typed value of a File sensor: metadata only. The file CONTENT is never part
    /// of any management API response.
    /// </summary>
    public sealed record FileValueDto
    {
        /// <summary>File name without extension.</summary>
        public string Name { get; init; }

        /// <summary>File extension (with the leading dot, may be empty).</summary>
        public string Extension { get; init; }

        /// <summary>Original file size in bytes.</summary>
        public long Size { get; init; }
    }
}
