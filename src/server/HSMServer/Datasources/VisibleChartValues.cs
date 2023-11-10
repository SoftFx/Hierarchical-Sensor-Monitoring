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


        internal abstract void Apply(BaseValue value);
    }


    public sealed class LineChartValue<T> : BaseChartValue where T : INumber<T>
    {
        private double _totalSum = 0.0;


        public T Value { get; private set; }


        internal LineChartValue(BaseValue<T> value)
        {
            Time = value.ReceivingTime;
            Value = value.Value;
        }


        internal override void Apply(BaseValue rawValue)
        {
            if (_countValues++ == 1)
                _totalSum += double.CreateChecked(Value);

            if (rawValue is BaseValue<T> value)
            {
                _totalSum += double.CreateChecked(value.Value);

                Value = T.CreateChecked(_totalSum / _countValues);
                Tooltip = $"Aggregated ({_countValues}) values";
            }
        }
    }
}