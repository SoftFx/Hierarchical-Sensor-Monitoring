using System.Threading;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;


namespace HSMDataCollector.Sensors
{
    internal sealed class MonitoringRateSensor : MonitoringSensorBase<double>, IMonitoringRateSensor
    {
        private SensorStatus _lastStatus = SensorStatus.Ok;
        private string _lastComment = string.Empty;
        private double _sum = 0.0;


        public MonitoringRateSensor(RateSensorOptions options) : base(options) { }


        protected override double GetValue()
        {
            var sec = PostTimePeriod.TotalSeconds;
            var value = sec > 0 ? _sum / sec : 0;

            Interlocked.Exchange(ref _sum, 0d);

            return value;
        }


        protected override SensorStatus GetStatus() => _lastStatus;

        protected override string GetComment() => _lastComment;


        public void AddValue(double value) => AddValue(value, SensorStatus.Ok, string.Empty);

        public void AddValue(double value, string comment = "") => AddValue(value, SensorStatus.Ok, comment);

        public void AddValue(double value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            Interlocked.Exchange(ref _sum, _sum + value);

            _lastComment = comment;
            _lastStatus = status;
        }
    }
}