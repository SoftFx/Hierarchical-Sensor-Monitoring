using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseChartValue
    {
        public DateTime Time { get; protected set; }

        public string Tooltip { get; protected set; }
    }


    public abstract class BaseChartValue<T> : BaseChartValue
    {
        public T Value { get; protected set; }


        internal abstract void ReapplyLast(T value, DateTime lastCollectedValue);

        internal abstract void Apply(T value, DateTime lastCollectedValue);
    }


    public sealed class LineChartValue<T> : BaseChartValue<T> where T : INumber<T>
    {
        private double _totalSum;
        private long _totalTime;

        private double _lastValue;
        private long _lastTime;

        private long _countValues;


        internal LineChartValue(BaseValue data, T value)
        {
            Apply(value, data.Time);
        }


        internal override void Apply(T value, DateTime lastCollectedValue)
        {
            _lastValue = double.CreateChecked(value);
            _lastTime = lastCollectedValue.Ticks;

            _totalSum += _lastValue;
            _totalTime += _lastTime;
            _countValues++;

            Value = T.CreateChecked(_totalSum / _countValues);
            Time = new DateTime(_totalTime / _countValues);

            Tooltip = $"Aggregated ({_countValues}) values";
        }

        internal override void ReapplyLast(T value, DateTime lastCollectedValue)
        {
            if (_countValues > 0)
            {
                _totalTime -= _lastTime;
                _totalSum -= _lastValue;
                _countValues--;
            }

            Apply(value, lastCollectedValue);
        }
    }
}