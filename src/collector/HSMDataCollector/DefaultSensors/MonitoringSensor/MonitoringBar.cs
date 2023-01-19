using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.DefaultSensors.MonitoringSensor
{
    public abstract class MonitoringBar<T> : BarSensorValueBase<T>
    {
        protected readonly List<T> _barValues = new List<T>();


        internal void Init(TimeSpan timerPeriod)
        {
            OpenTime = DateTime.UtcNow;
            CloseTime = OpenTime + timerPeriod;
        }

        internal void AddValue(T value)
        {
            if (DateTime.UtcNow <= CloseTime)
                _barValues.Add(value);
        }

        internal MonitoringBar<T> Complete()
        {
            Count = _barValues.Count;

            if (Count > 0)
            {
                LastValue = _barValues.LastOrDefault();

                _barValues.Sort();

                Min = _barValues.First();
                Max = _barValues.Last();
                Mean = CountMean();

                AddPercentile(_barValues, 0.25);
                AddPercentile(_barValues, 0.5);
                AddPercentile(_barValues, 0.75);
            }

            return this;
        }

        protected abstract T CountMean();

        private void AddPercentile(List<T> listValues, double percent)
        {
            var count = listValues.Count;
            var index = count > 1 ? (int)Math.Floor(count * percent) : 0;
            var percentile = count > 0 ? listValues[index] : default;

            Percentiles.Add(percent, percentile);
        }
    }


    public sealed class IntMonitoringBar : MonitoringBar<int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;


        protected override int CountMean() => _barValues.Sum() / Count;
    }


    public sealed class DoubleMonitoringBar : MonitoringBar<double>
    {
        public override SensorType Type => SensorType.DoubleBarSensor;


        protected override double CountMean() => _barValues.Sum() / Count;
    }
}
