using HSMCommon.Collections;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Datasources.Aggregators;
using System;
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

        private SourceSettings _settings;
        private BaseSensorModel _sensor;


        protected abstract BaseDataAggregator DataAggregator { get; }

        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        internal virtual SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            _settings = settings;
            _sensor = sensor;

            DataAggregator.Setup(settings);

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
            _newVisibleValues.Clear();

            DataAggregator.RecalculateStep(request);

            var history = await _sensor.GetHistoryData(request);

            history.Reverse();

            foreach (var raw in history)
                AddNewValue(raw);

            return new()
            {
                ChartType = DataAggregator.UseAggregation ? AggregatedType : NormalType,
                Values = _newVisibleValues.Cast<object>().ToList(),
            };
        }

        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.Cast<object>().ToList(),
                IsTimeSpan = _sensor.Type is SensorType.TimeSpan
            };


        public void Dispose()
        {
            _sensor.ReceivedNewValue -= AddNewValue;
        }


        private void AddNewValue(BaseValue value)
        {
            if (!DataAggregator.TryAddNewPoint(value, out var newPoint))
                return;

            _newVisibleValues.AddLast(newPoint);

            while (_newVisibleValues.Count > _settings.MaxVisibleCount)
                _newVisibleValues.RemoveFirst();
        }
    }
}