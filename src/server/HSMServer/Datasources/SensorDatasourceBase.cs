using HSMCommon.Collections;
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
        private const int MaxVisibleCnt = 100;

        private readonly CLinkedList<BaseChartValue> _newVisibleValues = new();
        private readonly CLinkedList<BaseChartValue> _curValues = new();

        private BaseSensorModel _sensor;

        private long _removedValuesCnt, _aggrValuesStep;
        private bool _aggreagateValues;

        protected PlottedProperty _plotProperty;


        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        protected abstract void AddVisibleValue(BaseValue baseValue);

        protected abstract void ApplyToLast(BaseValue newValue);


        public SensorDatasourceBase AttachSensor(BaseSensorModel sensor, PlottedProperty plotProperty)
        {
            _plotProperty = plotProperty;
            _sensor = sensor;

            _sensor.ReceivedNewValue += AddNewValue;

            return this;
        }


        public Task<InitChartSourceResponse> Initialize() =>
            Initialize(new SensorHistoryRequest
            {
                To = DateTime.UtcNow,
                Count = -MaxVisibleCnt,
            });

        public Task<InitChartSourceResponse> Initialize(DateTime from, DateTime to) =>
            Initialize(new SensorHistoryRequest
            {
                From = from,
                To = to,
                Count = -TreeValuesCache.MaxHistoryCount
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
                Values = _curValues.Cast<object>().ToList(),
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


        protected void AddVisibleToLast(BaseChartValue value)
        {
            _curValues.AddLast(value);
            _newVisibleValues.AddLast(value);
        }

        private void BuildInitialValues(List<BaseValue> rawList, SensorHistoryRequest request)
        {
            rawList.Reverse();

            _aggreagateValues = rawList.Count > MaxVisibleCnt;
            _aggrValuesStep = _aggreagateValues ? (request.To - request.From).Ticks / MaxVisibleCnt : 0L;
            _curValues.Clear();

            foreach (var raw in rawList)
                AddNewValue(raw);
        }

        private void AddNewValue(BaseValue value)
        {
            if (_aggreagateValues && _aggrValuesStep > 0)
            {
                if (_curValues.Count == 0 || _curValues.Last.Value.Time.Ticks + _aggrValuesStep < value.Time.Ticks)
                    AddVisibleValue(value);
                else
                    ApplyToLast(value);
            }
            else
                AddVisibleValue(value);

            while (_curValues.Count > MaxVisibleCnt)
            {
                _curValues.RemoveFirst();
                _removedValuesCnt = Math.Min(++_removedValuesCnt, MaxVisibleCnt);
            }

            while (_newVisibleValues.Count > MaxVisibleCnt)
                _newVisibleValues.RemoveFirst();
        }
    }
}