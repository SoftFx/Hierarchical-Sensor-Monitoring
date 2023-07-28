using HSMDataCollector.Bar;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder
{
    internal abstract class ListBarBuilder<T> : IBarBuilder<T, T>
    {
        protected readonly List<T> _barValues = new List<T>(1 << 6);

        public void AddValue(T value)
        {
            _barValues.Add(value);
        }

        public void FillBarFields(BarSensorValueBase<T> bar)
        {
            bar.Count = _barValues.Count;

            if (bar.Count > 0)
            {
                bar.LastValue = Round(_barValues.LastOrDefault());

                _barValues.Sort();

                bar.Min = Round(_barValues.First());
                bar.Max = Round(_barValues.Last());
                bar.Mean = Round(CountMean());

                AddPercentile(bar, _barValues, 0.25);
                AddPercentile(bar, _barValues, 0.5);
                AddPercentile(bar, _barValues, 0.75);
            }
        }

        private void AddPercentile(BarSensorValueBase<T> bar, List<T> listValues, double percent)
        {
            var count = listValues.Count;
            var index = count > 1 ? (int)Math.Floor(count * percent) : 0;
            var percentile = count > 0 ? listValues[index] : default;

            bar.Percentiles[percent] = Round(percentile);
        }

        protected abstract T CountMean();

        protected abstract T Round(T value);
    }

    internal sealed class IntListBarBuilder : ListBarBuilder<int>
    {
        protected override int CountMean() => _barValues.Sum() / _barValues.Count();

        protected override int Round(int value) => value;
    }

    internal sealed class DoubleListBarBuilder : ListBarBuilder<double>
    {
        private readonly int _precision;

        public DoubleListBarBuilder(int precision)
        {
            _precision = precision;
        }

        protected override double CountMean() => _barValues.Sum() / _barValues.Count();

        protected override double Round(double value) => Math.Round(value, _precision, MidpointRounding.AwayFromZero);
    }
}
