using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class Payload
    {
        public List<PayloadOption> Options { get; set; }


        public string Name { get; set; }

        public string Label { get; set; }

        public string Type { get; set; } = "select"; //available values "select" "multi-select"

        public string Placeholder { get; set; }

        public bool ReloadMetric { get; set; }


        public Payload() { }

        public Payload(string name)
        {
            Name = name;
            Label = name;
        }
    }
}
