using System;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class TargetMetric
    {
        public string Target { get; set; }

        public string RefId { get; set; }

        public SelectedPayload Payload { get; set; }


        public bool TryGetTargetAsId(out Guid id)
        {
            id = default;

            return Target != null && Guid.TryParse(Target, out id);
        }
    }
}