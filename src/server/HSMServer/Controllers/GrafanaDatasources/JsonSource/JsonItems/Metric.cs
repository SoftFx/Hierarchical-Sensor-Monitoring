using System;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class Metric
    {
        public const string SensorsPayloadName = nameof(SelectedPayload.Sensors);
        public const string TypePayloadName = nameof(SelectedPayload.Type);

        public static Payload[] BaseProductPayloads { get; } = new[]
        {
                new Payload(SensorsPayloadName)
                {
                    //Type = "multi-select",
                    Placeholder = "List of sensors open for Grafana",
                },

                new Payload(TypePayloadName),
        };


        public Payload[] Payloads { get; set; } = BaseProductPayloads;


        public string Label { get; set; }

        public string Value { get; set; }


        public Metric() { }

        public Metric(string label, string id)
        {
            Label = label;
            Value = id;
        }

        public Metric(string label, Guid id) : this(label, id.ToString()) { }
    }
}
