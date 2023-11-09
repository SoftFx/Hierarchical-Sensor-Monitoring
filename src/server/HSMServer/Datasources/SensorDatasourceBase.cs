using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Model.Model.History;
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
        private LinkedList<BaseChartValue> _curValues;
        private GetSensorHistoryRequest _request;
        private BaseSensorModel _sensor;


        protected abstract ChartType NormalType { get; }

        protected abstract ChartType AggreatedType { get; }


        public SensorDatasourceBase AttachSensor(BaseSensorModel sensor)
        {
            _sensor = sensor;
            _sensor.ReceivedNewValue += ApplyNewValue;

            return this;
        }

        public void Dispose()
        {
            _sensor.ReceivedNewValue -= ApplyNewValue;
        }


        public async Task<InitChartSourceResponse> GetInitializationData(int lastValuesCnt = 100)
        {
            var data = await _sensor.GetHistoryData(new SensorHistoryRequest
            {
                To = DateTime.UtcNow,
                Count = -lastValuesCnt,
            });

            _curValues = new LinkedList<BaseChartValue>(data.Select(Convert).Reverse());

            return new()
            {
                Values = _curValues.ToList(),
                ChartType = NormalType,
            };
        }

        public void Reload(GetSensorHistoryRequest request)
        {
            _request = request;
        }


        protected abstract BaseChartValue Convert(BaseValue baseValue);


        private void ApplyNewValue(BaseValue value)
        {

        }
    }


    public abstract record BaseChartSourceResponse
    {
       // public required Guid SourceId { get; init; }
    }


    public sealed record InitChartSourceResponse : BaseChartSourceResponse
    {
        public List<BaseChartValue> Values { get; init; }

        public ChartType ChartType { get; init; }
    }


    public sealed record UpdateChartSourceResponse : BaseChartSourceResponse
    {
        public int RemovedValuesCount { get; init; }
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