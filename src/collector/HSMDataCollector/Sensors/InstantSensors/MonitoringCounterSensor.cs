using System;
using System.Threading;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Sensors
{
    internal sealed class MonitoringRateSensor : MonitoringSensorBase<double, RateDisplayUnit>, IMonitoringRateSensor
    {
        private SensorStatus _lastStatus = SensorStatus.Ok;
        private string _lastComment = string.Empty;
        private double _sum = 0.0;


        public MonitoringRateSensor(RateSensorOptions options) : base(options) { }


        protected override double GetValue()
        {
            var sec = PostTimePeriod.TotalSeconds;
            var sum = Interlocked.Exchange(ref _sum, 0d);
            var value = sec > 0 ? sum / sec : 0;

            return value;
        }


        protected override SensorStatus GetStatus() => _lastStatus;

        protected override string GetComment() => _lastComment;


        public void AddValue(double value) => AddValue(value, SensorStatus.Ok, string.Empty);

        public void AddValue(double value, string comment = "") => AddValue(value, SensorStatus.Ok, comment);

        public void AddValue(double value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            try
            {
                AddToSum(value);

                _lastComment = comment;
                _lastStatus = status;
            }
            catch (Exception ex) { HandleException(ex); }
        }

        private void AddToSum(double value)
        {
            double current;
            double updated;

            do
            {
                current = _sum;
                updated = current + value;
            }
            while (!Interlocked.CompareExchange(ref _sum, updated, current).Equals(current));
        }
    }
}
