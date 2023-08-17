using System.Collections.Generic;
using System.ComponentModel;

namespace HSMSensorDataObjects.SensorRequests
{
    public enum Unit : int
    {
        bits = 0,
        bytes = 1,
        KB = 2,
        MB = 3,
        GB = 4,

        Percents = 100,
    }


    public sealed class AddOrUpdateSensorRequest : CommandRequestBase
    {
        [DefaultValue((int)Command.AddOrUpdateSensor)]
        public override Command Type => Command.AddOrUpdateSensor;


        public List<AlertUpdateRequest> Alerts { get; set; }

        public AlertUpdateRequest TtlAlert { get; set; }


        public SensorType? SensorType { get; set; }

        public string Description { get; set; }


        public long? KeepHistory { get; set; }

        public long? SelfDestroy { get; set; }

        public long? TTL { get; set; }


        public bool? AggregateData { get; set; }

        public bool? EnableGrafana { get; set; }

        public Unit? OriginalUnit { get; set; }
    }
}
