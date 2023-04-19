using System;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class Metric
    {
        public const string SensorsPayloadName = nameof(SelectedPayload.Sensor);
        public const string TypePayloadName = nameof(SelectedPayload.Type);

        public static Payload[] BaseProductPayloads { get; } = new[]
        {
                new Payload(SensorsPayloadName)
                {
                    Placeholder = "List of sensors open for Grafana",
                },

                new Payload(TypePayloadName)
                {
                    Placeholder = "Available data format for the sensor",
                },
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
