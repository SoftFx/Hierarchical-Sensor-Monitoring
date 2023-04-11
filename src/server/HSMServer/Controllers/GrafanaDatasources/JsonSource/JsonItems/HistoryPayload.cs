using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class HistoryPayload
    {
        public List<string> Sensors { get; set; }

        public string Type { get; set; }


        public bool IsFull => Sensors != null && !string.IsNullOrEmpty(Type);
    }
}