using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using System;
using System.Numerics;
using System.Text;

namespace HSMServer.Datasources
{
    public abstract class BaseChartValue
    {
        private static long _idCounter = 0L;


        public long Id { get; } = _idCounter++;

        public DateTime Time { get; protected set; }

        public string Tooltip { get; protected set; }


        internal abstract object Filter(PanelRangeSettings settings);
    }


    public abstract class BaseChartValue<T> : BaseChartValue
    {
        public T Value { get; protected set; }
    }


    public sealed class LineChartValue<T> : BaseChartValue<T> where T : INumber<T>
    {
        internal void SetNewState(ref readonly LinePointState<T> state)
        {
            Value = state.Value;
            Time = state.Time;

            var count = state.Count;

            Tooltip = count > 1 ? $"Aggregated ({count}) values" : string.Empty;
        }

        internal override object Filter(PanelRangeSettings settings)
        {
            var currentValue = double.CreateChecked(Value);

            if (currentValue > settings.MaxValue)
                Value = T.CreateChecked(settings.MaxValue);

            if (currentValue < settings.MinValue)
                Value = T.CreateChecked(settings.MinValue);

            var sb = new StringBuilder(1 << 4);

            sb.Append(currentValue != double.CreateChecked(Value) ? $"<br>Original value: {currentValue}" : currentValue);

            if (!string.IsNullOrEmpty(Tooltip))
                sb.Append($"<br>{Tooltip}");

            Tooltip = sb.ToString();

            return this;
        }
    }
}