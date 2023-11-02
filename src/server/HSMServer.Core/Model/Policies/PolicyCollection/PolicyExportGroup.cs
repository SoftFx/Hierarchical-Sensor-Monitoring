using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyExportGroup : ConcurrentDictionary<string, List<PolicyExportInfo>> { }


    public sealed record PolicyExportInfo(Policy Policy, string RelativeNodePath)
    {
        public string FullRelativePath => $"{RelativeNodePath}/{Policy.Sensor.DisplayName}".Trim('/');
    }
}