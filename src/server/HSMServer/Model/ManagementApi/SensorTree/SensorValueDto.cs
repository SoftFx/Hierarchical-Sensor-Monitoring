using System;
using System.ComponentModel;

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
    [Description("One sensor value (current or a history point): `value` is typed by the sensor type. Boolean/Integer/Double/Rate → a JSON number/boolean; String → string; TimeSpan → a .NET round-trip string (\"d.hh:mm:ss\"); Version → \"major.minor.build.revision\"; Enum → EnumValueDto; IntegerBar/DoubleBar → BarValueDto; File → FileValueDto (metadata only — file content is never served).")]
    public sealed record SensorValueDto
    {
        /// <summary>Value time as recorded by the collector (UTC, ISO 8601).</summary>
        [Description("Value time as recorded by the collector (UTC, ISO 8601).")]
        public DateTime Time { get; init; }

        /// <summary>
        /// Sensor status at this value, as the enum NAME (the wire value is the
        /// string, not the number). Value table: Ok, Error, OffTime.
        /// </summary>
        [Description("Sensor status at this value, as the enum NAME. Value table: Ok, Error, OffTime (OffTime marks timeout periods the sensor was silent).")]
        public string Status { get; init; }

        /// <summary>Comment attached to the value (may be null).</summary>
        [Description("Comment attached to the value (may be null).")]
        public string Comment { get; init; }

        /// <summary>The typed value — see the record summary for the per-type shapes.</summary>
        [Description("The typed value — see the SensorValueDto description for the per-type shapes.")]
        public object Value { get; init; }
    }


    /// <summary>Typed value of an Enum sensor: the raw int plus its resolved option label.</summary>
    [Description("Typed value of an Enum sensor: the raw int plus its resolved option label.")]
    public sealed record EnumValueDto
    {
        /// <summary>Raw enum int as sent by the collector.</summary>
        [Description("Raw enum int as sent by the collector.")]
        public int Value { get; init; }

        /// <summary>Option label from the sensor's enum options; null when the option is not registered.</summary>
        [Description("Option label from the sensor's enum options; null when the option is not registered.")]
        public string Label { get; init; }
    }


    /// <summary>Typed value of an IntegerBar/DoubleBar sensor: the statistics of one aggregation period.</summary>
    [Description("Typed value of an IntegerBar/DoubleBar sensor: the statistics of one aggregation period.")]
    public sealed record BarValueDto
    {
        /// <summary>Minimum of the period.</summary>
        [Description("Minimum of the period.")]
        public double Min { get; init; }

        /// <summary>Maximum of the period.</summary>
        [Description("Maximum of the period.")]
        public double Max { get; init; }

        /// <summary>Mean of the period.</summary>
        [Description("Mean of the period.")]
        public double Mean { get; init; }

        /// <summary>Number of aggregated values in the period.</summary>
        [Description("Number of aggregated values in the period.")]
        public int Count { get; init; }
    }


    /// <summary>
    /// Typed value of a File sensor: metadata only. The file CONTENT is never part
    /// of any management API response.
    /// </summary>
    [Description("Typed value of a File sensor: metadata only. The file CONTENT is never part of any management API response.")]
    public sealed record FileValueDto
    {
        /// <summary>File name without extension.</summary>
        [Description("File name without extension.")]
        public string Name { get; init; }

        /// <summary>File extension (with the leading dot, may be empty).</summary>
        [Description("File extension (with the leading dot, may be empty).")]
        public string Extension { get; init; }

        /// <summary>Original file size in bytes.</summary>
        [Description("Original file size in bytes.")]
        public long Size { get; init; }
    }
}
