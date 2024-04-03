using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal abstract class ConnectionsDifferenceSensor : BaseSocketsSensor
    {
        private int? _prevValue;


        internal ConnectionsDifferenceSensor(MonitoringInstantSensorOptions options) : base(options) { }


        protected override int GetValue()
        {
            var currentValue = base.GetValue();
            var returnValue = 0;

            if (_prevValue.HasValue)
                returnValue = currentValue - _prevValue.Value;

            _needSendValue = returnValue != 0;
            _prevValue = currentValue;

            return returnValue;
        }
    }
}