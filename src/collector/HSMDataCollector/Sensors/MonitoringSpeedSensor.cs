using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
namespace HSMDataCollector.Sensors
{
    public class MonitoringSpeedSensor : MonitoringSensorBase<double>, IInstantValueSensor<double>
    {
        private readonly object _lock = new object();


        private double _sum = 0d;
        private string _lastComment = string.Empty;
        private SensorStatus _lastStatus = SensorStatus.Ok;

        
        public MonitoringSpeedSensor(MonitoringInstantSensorOptions options) : base(options) { }
        
        
        protected override double GetValue()
        {
            var value = _sum / _receiveDataPeriod.Seconds;
            _sum = 0d;
            
            return value;
        }
        
        protected override string GetComment() => _lastComment;
        
        protected override SensorStatus GetStatus() => _lastStatus;
        
        public void AddValue(double value)
        {
            lock (_lock)
                _sum += value;
        }
        
        public void AddValue(double value, string comment = "")
        {
            AddValue(value);
            _lastComment = comment;
        }

        public void AddValue(double value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            AddValue(value, comment);
            _lastStatus = status;
        }
    }
}
