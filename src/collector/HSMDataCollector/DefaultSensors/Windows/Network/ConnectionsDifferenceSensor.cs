using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal abstract class ConnectionsDifferenceSensor : BaseSocketsSensor
    {
        private double? _prevValue;


        internal ConnectionsDifferenceSensor(MonitoringInstantSensorOptions options) : base(options) { }


        protected override double GetValue()
        {
            var currentValue = base.GetValue();
            var returnValue = 0d;

            if (_prevValue.HasValue)
                returnValue = currentValue - _prevValue.Value;

            _needSendValue = returnValue != 0;
            _prevValue = currentValue;

            return returnValue;
        }
    }
}