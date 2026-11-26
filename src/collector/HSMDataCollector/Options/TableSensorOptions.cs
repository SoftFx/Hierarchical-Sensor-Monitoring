using System;
using System.Collections.Generic;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Options
{
    /// <summary>
    /// The settings of a table sensor. A table sensor has no alerts, no TTL, no unit and no previous paths, and cannot
    /// be moved or renamed on the server: another shape or another place is another sensor.
    /// </summary>
    public sealed class TableSensorOptions
    {
        /// <summary>
        /// The value columns, in order, at least one. The time column is implicit and first; no column may be named
        /// <see cref="TableColumn.TimeColumnName"/>. Names are unique (ordinal), non-empty and at most 255 UTF-8 bytes.
        /// </summary>
        public List<TableColumn> Columns { get; } = new List<TableColumn>();

        public string Description { get; set; }

        /// <summary>
        /// How long the server keeps the rows; null takes the setting the sensor inherits from its folder and product.
        /// The server counts a table sensor's period in whole weeks, rounded up (a month is five weeks), and deletes by
        /// whole weeks: rows may stay up to a week longer than the period. The value is sent as it is set here.
        /// </summary>
        public TimeSpan? KeepHistory { get; set; }

        /// <summary>
        /// Where the path starts, as for every sensor: under the collector's computer and module
        /// (<see cref="SensorLocation.Module"/>, the default) or at the product root.
        /// </summary>
        public SensorLocation SensorLocation { get; set; }
    }
}
