using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System.Collections.Concurrent;
using System.Linq;

namespace HSMDataCollector.Sensors
{
    public class MonitoringSpeedSensor : MonitoringSensorBase<double>, IInstantValueSensor<double>
    {
        private readonly ConcurrentStack<double> _values = new ConcurrentStack<double>();
        
        
        private string _lastComment = string.Empty;
        private SensorStatus _lastStatus = SensorStatus.Ok;

        
        public MonitoringSpeedSensor(MonitoringInstantSensorOptions options) : base(options) { }
        
        
        protected override double GetValue()
        {
            var value = _values.Sum() / _receiveDataPeriod.Seconds;
            _values.Clear();
            
            return value;
        }
        
        protected override string GetComment() => _lastComment;
        
        protected override SensorStatus GetStatus() => _lastStatus;
        
        public void AddValue(double value)
        {
            _values.Push(value);
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
