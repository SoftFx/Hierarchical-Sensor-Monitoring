using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.DefaultSensors.Windows.Network
{
    internal abstract class ConnectionsDifferenceSensor : SocketsSensor
    {
        private double? _prevValue;


        internal ConnectionsDifferenceSensor(MonitoringInstantSensorOptions options) : base(options) { }

        
        protected override double GetValue()
        {
            var currentValue = base.GetValue();
            var returnValue = 0d;
            
            if (_prevValue.HasValue)
               returnValue = currentValue - _prevValue.Value;

            _prevValue = currentValue;
            
            return returnValue;
        }

        protected override string GetComment() => !_prevValue.HasValue ? "Calibration request" : base.GetComment();

        protected override SensorStatus GetStatus() => !_prevValue.HasValue ? SensorStatus.OffTime : base.GetStatus();
    }
}
