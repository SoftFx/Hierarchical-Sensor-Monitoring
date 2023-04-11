using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class Metric
    {
        public List<Payload> Payloads { get; set; } = new();


        public string Label { get; set; }

        public string Value { get; set; }


        public Metric() { }

        public Metric(string label, string id)
        {
            Label = label;
            Value = id;
        }


        public Metric Init(params Payload[] payloads)
        {
            Payloads = payloads.ToList();

            return this;
        }
    }
}
