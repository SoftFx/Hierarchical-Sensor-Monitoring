using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        private readonly LinkedList<BaseChartValue> _newVisibleValues = new();
        private LinkedList<BaseChartValue> _curValues;

        private BaseSensorModel _sensor;

        private long _removedValuesCnt = 0;
        private bool _aggreagateValues;


        protected abstract ChartType NormalType { get; }

        protected abstract ChartType AggreatedType { get; }


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
            });

        public async Task<InitChartSourceResponse> Initialize(SensorHistoryRequest request)
        {
            var data = await _sensor.GetHistoryData(request);

            _newVisibleValues.Clear();
            _removedValuesCnt = 0;

            _aggreagateValues = data.Count > MaxVisibleCnt;
            _curValues = new LinkedList<BaseChartValue>(data.Select(Convert).Reverse());

            return new()
            {
                ChartType = _aggreagateValues ? AggreatedType : NormalType,
                Values = _curValues.ToList(),
            };
        }


        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.ToList(),
                RemovedValuesCount = _removedValuesCnt,
            };


        public void Dispose()
        {
            _sensor.ReceivedNewValue -= AddNewValue;
        }


        private void AddNewValue(BaseValue value)
        {
            if (_aggreagateValues)
            {

            }
            else
            {
                var newVisibleValue = Convert(value);

                _curValues.AddLast(newVisibleValue);
                _newVisibleValues.AddLast(newVisibleValue);
            }

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


        protected override BaseChartValue Convert(BaseValue rawValue)
        {
            if (rawValue is BaseValue<T> value)
                return new LineChartValue<T>()
                {
                    Time = value.ReceivingTime,
                    Value = value.Value,
                };
            else
                return null;
        }
    }
}