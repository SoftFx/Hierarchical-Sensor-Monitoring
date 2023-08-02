using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorUpdateRequests
{
    public sealed class SensorUpdateRequest : BaseRequest
    {
        public List<AlertUpdateRequest> Policies { get; set; }

        public AlertUpdateRequest TTLPolicy { get; set; }


        public long KeepHistory { get; set; }

        public long SelfDestroy { get; set; }

        public long TTL { get; set; }


        public string Description { get; set; }

        public bool EnableGrafana { get; set; }
    }
}
