using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    public abstract class ConnectionsDifferenceSensor : BaseSocketsSensor
    {
        private int? _prevValue;


        internal ConnectionsDifferenceSensor(MonitoringInstantSensorOptions options) : base(options) { }


        protected override int? GetValue()
        {
            var value =  base.GetValue();
            if (!value.HasValue)
                return null;

            var currentValue = value.Value;
            var returnValue = 0;

            if (_prevValue.HasValue)
                returnValue = currentValue - _prevValue.Value;

            _prevValue = currentValue;

            if (returnValue != 0)
                return returnValue;
            else
                return null;
        }
    }
}