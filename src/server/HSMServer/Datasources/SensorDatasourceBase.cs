using HSMCommon.Collections;
using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Dashboards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Datasources
{
    public enum ChartType
    {
        Points,
        Line,
        Bars,
        StackedBars
    }


    public abstract class SensorDatasourceBase : IDisposable
    {
        private readonly CLinkedList<BaseChartValue> _newVisibleValues = new();
        private readonly CLinkedList<BaseChartValue> _curVisibleValues = new();

        private BaseSensorModel _sensor;

        private long _removedValuesCnt, _aggrValuesStep;
        private bool _aggreagateValues;

        protected BarBaseValue _lastBarValue; //TODO move to special derived class
        protected bool _isBarSensor;

        protected SourceSettings _settings;


        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        protected abstract void AddVisibleValue(BaseValue baseValue);

        protected abstract void ApplyToLast(BaseValue newValue);


        internal virtual SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            _settings = settings;
            _sensor = sensor;

            _isBarSensor = sensor.Type.IsBar();

            _sensor.ReceivedNewValue += AddNewValue;

            return this;
        }


        public Task<InitChartSourceResponse> Initialize() =>
            Initialize(new SensorHistoryRequest
            {
                To = DateTime.UtcNow,
                Count = -_settings.MaxVisibleCount,
            });

        public Task<InitChartSourceResponse> Initialize(DateTime from, DateTime to) =>
            Initialize(new SensorHistoryRequest
            {
                From = from,
                To = to,
                Count = -_settings.MaxVisibleCount,
            });

        public async Task<InitChartSourceResponse> Initialize(SensorHistoryRequest request)
        {
            var history = _sensor.GetHistoryData(request);

            if (history is not null)
            {
                var data = await history;

                BuildInitialValues(data, request);

                _newVisibleValues.Clear();
                _removedValuesCnt = 0;
            }

            return new()
            {
                ChartType = _aggreagateValues ? AggregatedType : NormalType,
                Values = _curVisibleValues.Cast<object>().ToList(),
            };
        }

        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.Cast<object>().ToList(),

                RemovedValuesCount = _removedValuesCnt,
                IsTimeSpan = _sensor.Type is SensorType.TimeSpan
            };


        public void Dispose()
        {
            _sensor.ReceivedNewValue -= AddNewValue;
        }


        protected bool IsPartialValueUpdate(BaseValue newValue)
        {
            return _isBarSensor && newValue is BarBaseValue barValue &&
                   _lastBarValue?.OpenTime == barValue?.OpenTime &&
                   _lastBarValue.CloseTime == barValue?.CloseTime;
        }

        protected void AddVisibleToLast(BaseChartValue value)
        {
            _curVisibleValues.AddLast(value);
            _newVisibleValues.AddLast(value);
        }

        private void BuildInitialValues(List<BaseValue> rawList, SensorHistoryRequest request)
        {
            rawList.Reverse();

            _aggreagateValues = rawList.Count > MaxVisibleCnt;
            _aggrValuesStep = _aggreagateValues ? (request.To - request.From).Ticks / MaxVisibleCnt : 0L;
            _curVisibleValues.Clear();

            foreach (var raw in rawList)
                AddNewValue(raw);
        }

        private void AddNewValue(BaseValue value)
        {
            if (IsPartialValueUpdate(value))
                ApplyToLast(value);
            else if (_aggreagateValues && _aggrValuesStep > 0)
            {
                if (_curVisibleValues.Count == 0 || _curVisibleValues.Last.Value.Time.Ticks + _aggrValuesStep < value.Time.Ticks)
                    AddVisibleValue(value);
                else
                    ApplyToLast(value);
            }
            else
                AddVisibleValue(value);

            while (_curVisibleValues.Count > MaxVisibleCnt)
            {
                _curVisibleValues.RemoveFirst();
                _removedValuesCnt = Math.Min(++_removedValuesCnt, MaxVisibleCnt);
            }

            while (_newVisibleValues.Count > MaxVisibleCnt)
                _newVisibleValues.RemoveFirst();
        }
    }
}