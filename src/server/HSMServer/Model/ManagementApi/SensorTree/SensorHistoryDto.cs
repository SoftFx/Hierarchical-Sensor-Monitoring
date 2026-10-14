using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// Sensor history over a time window: the NEWEST <c>maxPoints</c> values inside
    /// [<c>from</c>, <c>to</c>], oldest first. No server-side aggregation or
    /// decimation — when the window holds more values than requested, the oldest
    /// excess is dropped and <c>truncated</c> is set; narrow the window to see more.
    /// For aggregated (bar) sensors the response may carry one point older than the
    /// echoed <c>from</c> — the pre-window border value (only in an under-full
    /// response: a full one drops the border first, and dropping it alone never
    /// sets <c>truncated</c> — it is outside the window).
    /// </summary>
    [Description("Sensor history over a time window: the NEWEST maxPoints values inside [from, to], oldest first, no aggregation.")]
    public sealed record SensorHistoryDto
    {
        /// <summary>
        /// The returned values, oldest first (see <c>SensorValueDto</c>). Timeout
        /// markers — periods the sensor was silent — are included as OffTime points.
        /// </summary>
        [Description("The returned values, oldest first. Timeout markers — periods the sensor was silent — are included as OffTime points.")]
        public List<SensorValueDto> Points { get; init; } = [];

        /// <summary>Effective window start (UTC, ISO 8601) — the request value or the default.</summary>
        [Description("Effective window start (UTC, ISO 8601) — the request value or the default.")]
        public DateTime From { get; init; }

        /// <summary>Effective window end (UTC, ISO 8601) — the request value or the default.</summary>
        [Description("Effective window end (UTC, ISO 8601) — the request value or the default.")]
        public DateTime To { get; init; }

        /// <summary>Effective point limit (1..10000; 100 for File sensors).</summary>
        [Description("Effective point limit (1..10000; 100 for File sensors).")]
        public int MaxPoints { get; init; }

        /// <summary>True when the window held more values than returned — narrow the window for full resolution.</summary>
        [Description("True when the window held more values than returned — narrow the window for full resolution.")]
        public bool Truncated { get; init; }

        /// <summary>
        /// True only for File sensors whose history another request is currently
        /// reading (their reads are serialized per sensor): the server could not
        /// read this sensor's values — retry shortly. Never true for other sensor
        /// types; an empty points list without this flag means the window holds
        /// no values.
        /// </summary>
        [Description("True only for File sensors whose history another request is currently reading: the server could not read this sensor's values — retry shortly. An empty points list without this flag means the window holds no values.")]
        public bool ReadUnavailable { get; init; }
    }
}
