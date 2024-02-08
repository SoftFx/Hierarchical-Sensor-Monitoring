using System;
using System.Collections.Generic;

namespace HSMServer.Core.StatisticInfo
{
    public sealed record NodeHistoryInfo
    {
        public Dictionary<Guid, NodeHistoryInfo> SubnodesInfo { get; } = [];

        public Dictionary<Guid, SensorHistoryInfo> SensorsInfo { get; } = [];
    }
}