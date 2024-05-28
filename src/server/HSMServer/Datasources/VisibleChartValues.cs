using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using HSMServer.Extensions;
using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace HSMServer.Datasources
{
    public abstract class BaseChartValue
    {
        public DateTime Time { get; protected set; }

        public string Tooltip { get; protected set; }


        internal abstract string GetValue();
        
        internal abstract object Filter(PanelRangeSettings settings);
    }


    public abstract class BaseChartValue<T> : BaseChartValue
    {
        public T Value { get; protected set; }

        internal override string GetValue() => Value.ToString();
    }


    public sealed class VersionChartValue : BaseChartValue<Version>, ILinePoint<VersionPointState>
    {
        public void SetNewState(ref readonly VersionPointState state)
        {
            Value = state.Value;
            Time = state.Time;

            var sb = new StringBuilder(1 << 4);

            foreach (var (time, version) in state.AggrState.Reverse())
                sb.Append($"{time.ToDefaultFormat()} - {version.RemoveTailZeroes()}");

            Tooltip = sb.ToString();
        }


        internal override object Filter(PanelRangeSettings _) => this;
    }


    public sealed class LineChartValue<T> : BaseChartValue<T>, ILinePoint<LineNumberPointState<T>>
        where T : INumber<T>
    {
        public void SetNewState(ref readonly LineNumberPointState<T> state)
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