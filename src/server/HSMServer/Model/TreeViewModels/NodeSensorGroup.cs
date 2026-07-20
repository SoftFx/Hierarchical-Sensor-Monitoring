using HSMCommon.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModel
{
    /// <summary>
    /// A set of a node's descendant sensors that share the same sensor type and effective unit,
    /// and can therefore be overlaid on a single multi-line time chart (see issue #1235).
    /// </summary>
    public sealed record NodeSensorGroup(SensorType Type, int? UnitCode, string UnitLabel, List<Guid> SensorIds)
    {
        /// <summary>Stable identifier of the (type, unit) group — used by the client's group selector.</summary>
        public string Key => $"{(int)Type}:{(UnitCode.HasValue ? UnitCode.Value.ToString() : "-")}";
    }
}
