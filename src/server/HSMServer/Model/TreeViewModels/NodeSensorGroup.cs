using HSMCommon.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModel
{
    /// <summary>
    /// A set of a node's descendant sensors that share the same sensor type and effective unit,
    /// and can therefore be overlaid on a single multi-line time chart (see issue #1235).
    /// </summary>
    public sealed record NodeSensorGroup(SensorType Type, string UnitLabel, List<Guid> SensorIds);
}
