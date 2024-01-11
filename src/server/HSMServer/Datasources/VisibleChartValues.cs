using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseChartValue
    {
        private static long _idCounter = 0L;


        public long Id { get; } = _idCounter++;

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
        private double _totalValueSum;
        private double _totalTimeSum;

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

            _totalValueSum += _lastValue;
            _totalTimeSum += _lastTime;
            _countValues++;

            Value = T.CreateChecked(_totalValueSum / _countValues);
            Time = new DateTime((long)(_totalTimeSum / _countValues));

            Tooltip = _countValues > 1 ? $"Aggregated ({_countValues}) values" : string.Empty;
        }

        internal override void ReapplyLast(T value, DateTime lastCollectedValue)
        {
            if (_countValues > 0)
            {
                _totalValueSum -= _lastValue;
                _totalTimeSum -= _lastTime;
                _countValues--;
            }

            Apply(value, lastCollectedValue);
        }
    }
}