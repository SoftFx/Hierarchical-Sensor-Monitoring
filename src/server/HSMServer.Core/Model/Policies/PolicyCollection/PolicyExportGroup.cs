using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyExportGroup : ConcurrentDictionary<string, List<PolicyExportInfo>>
    {
        public string CurrentProduct { get; private set; }


        public PolicyExportGroup SetProduct(string product)
        {
            CurrentProduct = product;

            return this;
        }
    }


    public sealed record PolicyExportInfo(Policy Policy, string RelativeNodePath, string ProductName)
    {
        public string FullRelativePath => $"{RelativeNodePath}/{Policy.Sensor.DisplayName}".Trim('/');
    }
}