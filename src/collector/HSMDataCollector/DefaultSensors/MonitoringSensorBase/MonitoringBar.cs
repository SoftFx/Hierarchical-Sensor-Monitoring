using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringBarBase<T> : BarSensorValueBase<T>
    {
        protected readonly List<T> _barValues = new List<T>(1 << 6);


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

        internal MonitoringBarBase<T> Complete()
        {
            Count = _barValues.Count;

            if (Count > 0)
            {
                LastValue = Round(_barValues.LastOrDefault());

                _barValues.Sort();

                Min = Round(_barValues.First());
                Max = Round(_barValues.Last());
                Mean = Round(CountMean());

                AddPercentile(_barValues, 0.25);
                AddPercentile(_barValues, 0.5);
                AddPercentile(_barValues, 0.75);
            }

            return this;
        }

        protected abstract T CountMean();

        protected abstract T Round(T value);

        private void AddPercentile(List<T> listValues, double percent)
        {
            var count = listValues.Count;
            var index = count > 1 ? (int)Math.Floor(count * percent) : 0;
            var percentile = count > 0 ? listValues[index] : default;

            Percentiles.Add(percent, Round(percentile));
        }
    }


    public sealed class IntMonitoringBar : MonitoringBarBase<int>
    {
        public override SensorType Type => SensorType.IntegerBarSensor;


        protected override int CountMean() => _barValues.Sum() / Count;

        protected override int Round(int value) => value;
    }


    public sealed class DoubleMonitoringBar : MonitoringBarBase<double>
    {
        private const int Precision = 2;


        public override SensorType Type => SensorType.DoubleBarSensor;


        protected override double CountMean() => _barValues.Sum() / Count;

        protected override double Round(double value) => Math.Round(value, Precision, MidpointRounding.AwayFromZero);
    }
}
