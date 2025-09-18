using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorAlive : MonitoringSensorBase<bool, NoDisplayUnit>
    {
        private bool _firstData = true;

        public CollectorAlive(MonitoringInstantSensorOptions options) : base(options) { }


        protected override bool GetValue()
        {
            if (_firstData)
            {
                _firstData = false;
                return false;
            }

            return true;
        }
    }
}
