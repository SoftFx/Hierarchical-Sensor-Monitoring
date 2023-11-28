using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using HSMServer.Core.Cache;

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


        protected abstract ChartType AggreatedType { get; }

        protected abstract ChartType NormalType { get; }


        protected abstract BaseChartValue Convert(BaseValue baseValue);


        public SensorDatasourceBase AttachSensor(BaseSensorModel sensor)
        {
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
                ChartType = _aggreagateValues ? AggreatedType : NormalType,
                Values = _curValues.ToList(),
            };
        }

        public (string Path, SensorType Type, Unit? unit) GetSourceInfo() => (_sensor.Path, _sensor.Type, _sensor.OriginalUnit);

        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.Cast<object>().ToList(),
                RemovedValuesCount = _removedValuesCnt,
            };


        public void Dispose()
        {
            _sensor.ReceivedNewValue -= AddNewValue;
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
            void AddVisibleValue()
            {
                var newVisibleValue = Convert(value);

                _curValues.AddLast(newVisibleValue);
                _newVisibleValues.AddLast(newVisibleValue);
            }

            if (_aggreagateValues && _aggrValuesStep > 0)
            {
                if (_curValues.Count == 0 || _curValues.Last.Value.Time.Ticks + _aggrValuesStep < value.Time.Ticks)
                    AddVisibleValue();
                else
                    _curValues.Last.Value.Apply(value);
            }
            else
                AddVisibleValue();

            while (_curValues.Count > MaxVisibleCnt)
            {
                _curValues.RemoveFirst();
                _removedValuesCnt = Math.Min(++_removedValuesCnt, MaxVisibleCnt);
            }

            while (_newVisibleValues.Count > MaxVisibleCnt)
                _newVisibleValues.RemoveFirst();
        }
    }


    public sealed class PointDatasource : SensorDatasourceBase
    {
        protected override ChartType NormalType => ChartType.Points;

        protected override ChartType AggreatedType => ChartType.StackedBars;


        protected override BaseChartValue Convert(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }
    }


    public sealed class BarsDatasource : SensorDatasourceBase
    {
        protected override ChartType NormalType => ChartType.Bars;

        protected override ChartType AggreatedType => ChartType.Bars;


        protected override BaseChartValue Convert(BaseValue baseValue)
        {
            throw new NotImplementedException();
        }
    }


    public sealed class LineDatasource<T> : SensorDatasourceBase where T : INumber<T>
    {
        protected override ChartType NormalType => ChartType.Line;

        protected override ChartType AggreatedType => ChartType.Line;


        protected override BaseChartValue Convert(BaseValue rawValue) =>
            rawValue is BaseValue<T> value ? new LineChartValue<T>(value) : null;
    }
}