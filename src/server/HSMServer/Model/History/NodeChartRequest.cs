using System;

namespace HSMServer.Model.Model.History
{
    /// <summary>
    /// Request for the node-level overlay chart (issue #1235): one node id plus the operator-chosen
    /// time window. The server groups the node's comparable descendants by (type, unit); <c>GroupKey</c>
    /// selects which group to chart (default: the largest).
    /// </summary>
    public sealed record NodeChartRequest
    {
        public string NodeId { get; set; }

        /// <summary>
        /// Stable key of the comparable group to overlay (see <c>NodeSensorGroup.Key</c>). When null/unknown
        /// the server falls back to the largest comparable group.
        /// </summary>
        public string GroupKey { get; set; }

        public DateTime From { get; set; } = DateTime.MinValue;

        public DateTime To { get; set; } = DateTime.MaxValue;
    }
}
