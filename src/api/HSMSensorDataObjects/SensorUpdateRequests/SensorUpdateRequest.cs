using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorUpdateRequests
{
    public enum Unit : byte
    {
        Byte = 0,
        Kilobyte = 1,
        Megabyte = 2,
        Gigabyte = 3,
        Percent = 30,
    }


    public sealed class SensorUpdateRequest : BaseRequest
    {
        public List<AlertUpdateRequest> Policies { get; set; }

        public AlertUpdateRequest TTLPolicy { get; set; }

        public List<Unit> AvailableUnites { get; set; }


        public long KeepHistory { get; set; }

        public long SelfDestroy { get; set; }

        public long TTL { get; set; }


        public bool SaveOnlyUniqueValues { get; set; }

        public bool EnableGrafana { get; set; }

        public string Description { get; set; }

        public Unit SelectedUnit { get; set; }
    }
}
