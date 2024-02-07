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

        private long _maxVisiblePoints, _aggrStepTicks, _aggrEndOfPeriod;


        internal bool UseAggregation { get; private set; }


        protected abstract void ReapplyValue(BaseChartValue point, BaseValue newValue);

        protected abstract void ApplyValue(BaseChartValue point, BaseValue newValue);

        protected abstract BaseChartValue GetNewChartValue(BaseValue value);


        internal virtual void Setup(SourceSettings settings)
        {
            _maxVisiblePoints = settings.MaxVisibleCount;
            _useReapplyRule = GetReapplyRule(settings);

            UseAggregation = settings.AggregateValues;
        }

        internal virtual void RecalculateAggrSections(SensorHistoryRequest request)
        {
            _aggrStepTicks = _maxVisiblePoints > 0 && request.From != DateTime.MinValue ? (request.To - request.From).Ticks / _maxVisiblePoints : 0; //disable aggr for Edit panel request (from == MinValue)
            _aggrEndOfPeriod = request.From.Ticks + _aggrStepTicks;

            _lastSensorValue = null;
            _lastChartValue = null;
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

                if (UseAggregation && newValue.Time.Ticks <= _aggrEndOfPeriod)
                {
                    ApplyValue(_lastChartValue, newValue);
                    return false;
                }
            }

            updatedPoint = CalculateNewPoint(newValue);

            return true;
        }


        private BaseChartValue CalculateNewPoint(BaseValue newValue)
        {
            _lastChartValue = GetNewChartValue(newValue);

            var time = newValue.Time.Ticks;

            if (UseAggregation)
            {
                if (time > _aggrEndOfPeriod && _aggrStepTicks > 0) //protection for first value
                {
                    var diff = time - _aggrEndOfPeriod;
                    var cntSteps = diff / _aggrStepTicks;

                    if (diff % _aggrStepTicks != 0) //check on right max border
                        cntSteps++;

                    _aggrEndOfPeriod += cntSteps * _aggrStepTicks;
                }
            }
            else
                _aggrEndOfPeriod = time;

            return _lastChartValue;
        }


        private static Func<BaseValue, BaseValue, bool> GetReapplyRule(SourceSettings settings) => settings.SensorType switch
        {
            SensorType.IntegerBar or SensorType.DoubleBar => (o, n) => IsPartialUpdate(o, (BarBaseValue)n),
            _ => (_, _) => false,
        };

        private static bool IsPartialUpdate(BaseValue oldValue, BarBaseValue newValue) => oldValue is BarBaseValue oldBarValue && oldBarValue.IsUpdatedBar(newValue);
    }
}