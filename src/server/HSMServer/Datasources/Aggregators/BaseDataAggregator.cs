using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;

namespace HSMServer.Datasources.Aggregators
{
    public abstract class BaseDataAggregator
    {
        private Func<BaseValue, BaseValue, bool> _useReapplyRule;

        private BaseChartValue _lastChartValue;
        private BaseValue _lastSensorValue;

        private long _maxVisiblePoints;
        private long _aggrStepTicks;


        internal bool UseAggregation { get; private set; }


        protected abstract void ReapplyValue(BaseChartValue point, BaseValue newValue);

        protected abstract void ApplyValue(BaseChartValue point, BaseValue newValue);

        protected abstract BaseChartValue BuildChartValue(BaseValue value);


        internal virtual void Setup(SourceSettings settings)
        {
            _maxVisiblePoints = settings.MaxVisibleCount;
            _useReapplyRule = GetReapplyRule(settings);

            UseAggregation = settings.AggregateValues;
        }

        internal void RecalculateStep(SensorHistoryRequest request)
        {
            _aggrStepTicks = _maxVisiblePoints > 0 ? (request.To - request.From).Ticks / _maxVisiblePoints : 0;
        }

        internal bool TryAddNewPoint(BaseValue newValue, out BaseChartValue updatedPoint)
        {
            var oldValue = _lastSensorValue;
            updatedPoint = _lastChartValue;

            _lastSensorValue = newValue;

            if (_lastChartValue is not null)
            {
                if (_useReapplyRule(oldValue, newValue))
                {
                    ReapplyValue(_lastChartValue, newValue);
                    return false;
                }

                if (UseAggregation && _lastChartValue?.Time.Ticks + _aggrStepTicks >= newValue.Time.Ticks)
                {
                    ApplyValue(_lastChartValue, newValue);
                    return false;
                }
            }

            _lastChartValue = BuildChartValue(newValue);
            updatedPoint = _lastChartValue;

            return true;
        }


        private static Func<BaseValue, BaseValue, bool> GetReapplyRule(SourceSettings settings) => settings.SensorType switch
        {
            SensorType.IntegerBar or SensorType.DoubleBar => (o, n) => IsPartialUpdate(o, (BarBaseValue)n),
            _ => (_, _) => false,
        };

        private static bool IsPartialUpdate(BaseValue oldValue, BarBaseValue newValue) => oldValue is BarBaseValue oldBarValue && oldBarValue.IsUpdatedBar(newValue);
    }
}