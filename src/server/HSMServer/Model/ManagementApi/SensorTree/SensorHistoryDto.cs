using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Sensor history over a time window: the NEWEST <c>maxPoints</c> values inside
    /// [<c>from</c>, <c>to</c>], oldest first. No server-side aggregation or
    /// decimation — when the window holds more values than requested, the oldest
    /// excess is dropped and <c>truncated</c> is set; narrow the window to see more.
    /// </summary>
    public sealed record SensorHistoryDto
    {
        /// <summary>
        /// The returned values, oldest first (see <c>SensorValueDto</c>). Timeout
        /// markers — periods the sensor was silent — are included as OffTime points.
        /// </summary>
        public List<SensorValueDto> Points { get; init; } = [];

        /// <summary>Effective window start (UTC, ISO 8601) — the request value or the default.</summary>
        public DateTime From { get; init; }

        /// <summary>Effective window end (UTC, ISO 8601) — the request value or the default.</summary>
        public DateTime To { get; init; }

        /// <summary>Effective point limit (1..10000).</summary>
        public int MaxPoints { get; init; }

        /// <summary>True when the window held more values than returned — narrow the window for full resolution.</summary>
        public bool Truncated { get; init; }
    }
}
