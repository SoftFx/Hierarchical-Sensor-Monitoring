using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;

namespace HSMServer.Datasources.Aggregators
{
    public abstract class BaseDataAggregator
    {
        private Predicate<BaseValue> _useReapplyRule;

        private BaseChartValue _lastChartValue;
        private BaseValue _lastSensorValue;

        private long _maxVisiblePoints;
        private long _aggrStepTicks;


        internal bool UseAggregation { get; private set; }


        internal void Setup(SourceSettings settings)
        {
            _maxVisiblePoints = settings.MaxVisibleCount;
            _useReapplyRule = GetReapplyRule(settings);

            UseAggregation = settings.AggregateValues;
        }

        internal void RecalculateStep(SensorHistoryRequest request)
        {
            _aggrStepTicks = _maxVisiblePoints > 0 ? (request.To - request.From).Ticks / _maxVisiblePoints : 0;
        }

        internal bool TryAddNewPoint(BaseValue newValue, out BaseChartValue lastPoint)
        {
            lastPoint = _lastChartValue;

            if (_useReapplyRule(newValue))
            {
                //reapply
                return false;
            }

            if (UseAggregation && _lastChartValue?.Time.Ticks + _aggrStepTicks >= newValue.Time.Ticks)
            {
                //apply
                return false;
            }

            //AddNewValue

            return true;
        }


        private Predicate<BaseValue> GetReapplyRule(SourceSettings settings) => settings.SensorType switch
        {
            SensorType.IntegerBar or SensorType.DoubleBar => (newValue) => IsPartialUpdate((BarBaseValue)newValue),
            _ => _ => false,
        };

        private bool IsPartialUpdate(BarBaseValue newValue) => _lastSensorValue is BarBaseValue oldBarValue && oldBarValue.IsUpdatedBar(newValue);
    }
}