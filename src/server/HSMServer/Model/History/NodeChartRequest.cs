using System;

namespace HSMServer.Model.Model.History
{
    /// <summary>
    /// Request for the node-level overlay chart (issue #1235): one node id plus the operator-chosen
    /// time window. The server fans out over the node's largest comparable child-sensor group.
    /// </summary>
    public sealed record NodeChartRequest
    {
        public string NodeId { get; set; }

        public DateTime From { get; set; } = DateTime.MinValue;

        public DateTime To { get; set; } = DateTime.MaxValue;
    }
}
