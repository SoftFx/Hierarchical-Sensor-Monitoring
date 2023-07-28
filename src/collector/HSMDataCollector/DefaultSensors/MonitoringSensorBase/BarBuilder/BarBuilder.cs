using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMDataCollector.DefaultSensors.MonitoringSensorBase.BarBuilder
{
    public abstract class BarBuilder<T>
        where T : IComparable<T>
    {
        private BarValue<T> _currentValue;

        public void AddValue(BarValue<T> value)
        {
            if (value.Count == 0)
                return;
            var mean = CountMean(_currentValue.Mean, _currentValue.Count, value.Mean, value.Count);
            _currentValue = _currentValue.Merge(value).WithMean(mean);
        }

        public void AddValue(T value)
        {
            var mean = CountMean(_currentValue.Mean, _currentValue.Count, value, 1);
            _currentValue = _currentValue.AddValue(value).WithMean(mean);
        }

        public void AddValues(IEnumerable<T> values)
        {
            var valueToAdd = new BarValue<T>();
            foreach (var value in values)
            {
                valueToAdd = valueToAdd.AddValue(value);
            }
            AddValue(valueToAdd.WithMean(CountMean(values)));
        }

        public void FillBarFields(BarSensorValueBase<T> bar)
        {
            bar.Count = _currentValue.Count;

            if (bar.Count > 0)
            {
                bar.LastValue = Round(_currentValue.LastValue);

                bar.Min = Round(_currentValue.MinValue);
                bar.Max = Round(_currentValue.MaxValue);
                bar.Mean = Round(_currentValue.Mean);

                bar.Percentiles[0.25] = Round(CountMean(_currentValue.MinValue, 1, _currentValue.Mean, 1));
                bar.Percentiles[0.5] = Round(_currentValue.Mean);
                bar.Percentiles[0.75] = Round(CountMean(_currentValue.MaxValue, 1, _currentValue.Mean, 1));
            }
        }

        protected abstract T CountMean(T prevMean, int prevCount, T addMean, int addCount);
        protected abstract T CountMean(IEnumerable<T> values); 

        protected abstract T Round(T value);

        public BarValue<T> GetCurrentAndReset()
        {
            var result = _currentValue;
            _currentValue = default;
            return result;
        }
    }

    internal sealed class IntBarBuilder : BarBuilder<int>
    {
        protected override int CountMean(int prevMean, int prevCount, int addMean, int addCount) => (prevMean * prevCount + addMean * addCount) / (prevCount + addCount);

        protected override int CountMean(IEnumerable<int> values)
        {
            int sum = 0;
            int count = 0;
            foreach (var value in values)
            {
                sum += value;
                count++;
            }
            return sum / count;
        }

        protected override int Round(int value) => value;
    }

    internal sealed class DoubleBarBuider : BarBuilder<double>
    {
        private readonly int _precision;

        public DoubleBarBuider(int precision)
        {
            _precision = precision;
        }

        protected override double CountMean(double prevMean, int prevCount, double addMean, int addCount) => (prevMean * prevCount + addMean * addCount) / (prevCount + addCount);

        protected override double CountMean(IEnumerable<double> values)
        {
            double sum = 0;
            int count = 0;
            foreach (var value in values)
            {
                sum += value;
                count++;
            }
            return sum / count;
        }

        protected override double Round(double value) => Math.Round(value, _precision, MidpointRounding.AwayFromZero);
    }
}
