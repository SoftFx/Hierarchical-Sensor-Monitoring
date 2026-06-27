using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorGroupsRequest
    {
        public Guid ProductId { get; set; }

        /// Sensor group names that should be disabled. All others are considered enabled.
        /// Valid values: "computer", "system", "disk", "network", "module", "process".
        public List<string> DisabledGroups { get; set; }
    }
}
