using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseChartValue
    {
        protected long _countValues = 1;


        public DateTime Time { get; protected set; }

        public string Tooltip { get; protected set; }
    }


    public abstract class BaseChartValue<T> : BaseChartValue
    {
        public T Value { get; protected set; }


        internal abstract void Apply(T value);
    }


    public sealed class LineChartValue<T> : BaseChartValue<T> where T : INumber<T>
    {
        private double _totalSum = 0.0;


        internal LineChartValue(BaseValue data, T value)
        {
            Time = data.Time;
            Value = value;
        }


        internal override void Apply(T value)
        {
            if (_countValues++ == 1)
                _totalSum += double.CreateChecked(Value);

            _totalSum += double.CreateChecked(value);

            Value = T.CreateChecked(_totalSum / _countValues);
            Tooltip = $"Aggregated ({_countValues}) values";
        }
    }


    public sealed class TimeSpanChartValue : BaseChartValue<TimeSpan>
    {
        private double _totalTicks = 0.0;


        public TimeSpanChartValue(TimeSpanValue value)
        {
            Time = value.Time;
            Value = value.Value;
        }


        internal override void Apply(TimeSpan value)
        {
            if (_countValues++ == 1)
                _totalTicks += double.CreateChecked(Value.Ticks);

            _totalTicks += double.CreateChecked(value.Ticks);

            Value = TimeSpan.FromTicks(long.CreateChecked(_totalTicks / _countValues));
            Tooltip = $"Aggregated ({_countValues}) values";
        }
    }
}