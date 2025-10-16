using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HSMServer.DTOs
{
    public class AddOrUpdateSensorRequestDto : CommandRequestBase
    {

        [DefaultValue((int)Command.AddOrUpdateSensor)]
        public override Command Type => Command.AddOrUpdateSensor;

        public List<AlertUpdateRequest> Alerts { get; set; }

        public AlertUpdateRequest TtlAlert { get; set; }

        public SensorType? SensorType { get; set; }

        public string Description { get; set; }

        [Obsolete("This setting doesn't exist for sensor now")]
        public DefaultChatsMode? DefaultChats { get; set; }

        public long? KeepHistory { get; set; }

        public long? SelfDestroy { get; set; }

        public long? TTL { get; set; }

        public StatisticsOptions? Statistics { get; set; }

        public bool? IsSingletonSensor { get; set; }

        public bool? AggregateData { get; set; }


        public bool? EnableGrafana { get; set; }

        public Unit? OriginalUnit { get; set; }

        public int? DisplayUnit { get; set; }

        public DefaultAlertsOptions DefaultAlertsOptions { get; set; }

        public bool IsForceUpdate { get; set; }

        public List<EnumOption> EnumOptions { get; set; }
    }
}
