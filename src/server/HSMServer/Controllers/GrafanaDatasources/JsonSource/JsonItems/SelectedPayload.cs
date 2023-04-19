using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class SelectedPayload
    {
        public string Sensor { get; set; }

        public string Type { get; set; }


        public bool IsFull => Sensor != null && !string.IsNullOrEmpty(Type);
    }
}