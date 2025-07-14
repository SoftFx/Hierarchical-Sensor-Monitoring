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
            try
            {
                double currentValue = double.CreateChecked(Value);
                double newValue = currentValue;
                bool valueChanged = false;

                if (currentValue > settings.MaxValue)
                {
                    newValue = settings.MaxValue;
                    valueChanged = true;
                }
                else if (currentValue < settings.MinValue)
                {
                    newValue = settings.MinValue;
                    valueChanged = true;
                }

                if (valueChanged)
                {
                    Value = T.CreateChecked(newValue);
                }

                var sb = new StringBuilder(64);

                if (valueChanged)
                {
                    sb.Append("<br>Original value: ").Append(currentValue);
                }
                else
                {
                    sb.Append(currentValue);
                }

                if (!string.IsNullOrEmpty(Tooltip))
                    sb.Append("<br>").Append(Tooltip);

                Tooltip = sb.ToString();

                return this;
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException($"Value conversion overflow: {Value}", ex);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"Invalid value format: {Value}", ex);
            }
        }
    }
}