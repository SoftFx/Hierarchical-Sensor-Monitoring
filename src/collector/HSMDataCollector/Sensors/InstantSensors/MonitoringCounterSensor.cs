using System;
using System.Diagnostics;
using System.Threading;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Extensions;
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
        private long _previousSampleTimestamp;

        // Monotonic time source; replaced in tests to simulate sleep/suspend gaps (#1102-E2).
        internal Func<long> TimestampProvider = Stopwatch.GetTimestamp;


        public MonitoringRateSensor(RateSensorOptions options) : base(options) { }


        public override System.Threading.Tasks.ValueTask<bool> InitAsync()
        {
            // A restart must not inherit the previous run's baseline: the first sample of the new
            // run would otherwise divide by the whole stopped gap and deflate the rate.
            Interlocked.Exchange(ref _previousSampleTimestamp, 0);

            return base.InitAsync();
        }


        protected override double GetValue()
        {
            var now = TimestampProvider();
            var previous = Interlocked.Exchange(ref _previousSampleTimestamp, now);
            var sum = Interlocked.Exchange(ref _sum, 0d);

            // Divide by the time that actually elapsed since the previous sample, not by the
            // configured period (#1102-E2): after machine sleep/suspend the scheduler skips missed
            // ticks, so the accumulated sum spans the whole gap. The first sample (no previous
            // timestamp) and a zero/negative gap fall back to the configured period.
            var sec = previous != 0 && now > previous
                ? (now - previous) / (double)Stopwatch.Frequency
                : PostTimePeriod.TotalSeconds;

            return sec > 0 ? sum / sec : 0;
        }


        protected override SensorStatus GetStatus() => _lastStatus;

        protected override string GetComment() => _lastComment;


        public void AddValue(double value) => AddValue(value, SensorStatus.Ok, string.Empty);

        public void AddValue(double value, string comment = "") => AddValue(value, SensorStatus.Ok, comment);

        public void AddValue(double value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            try
            {
                if (!SensorValueExtensions.IsValidValue(value, status))
                {
                    _dataProcessor.LogDroppedValue(SensorPath, $"rate increment failed validation (status: {status})");
                    return;
                }

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
