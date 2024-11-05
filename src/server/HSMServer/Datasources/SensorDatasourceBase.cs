using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Datasources.Aggregators;
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

        private Func<BaseChartValue, object> _filter;
        private SourceSettings _settings;
        private BaseSensorModel _sensor;


        protected abstract BaseDataAggregator DataAggregator { get; }

        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        internal virtual SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            static object DefaultFilter(BaseChartValue value) => value;

            _settings = settings;
            _sensor = sensor;

            _filter = _settings.IsDefaultFilter ? DefaultFilter : value => value.Filter(_settings.YRange);

            DataAggregator.Setup(settings);

            _sensor.ReceivedNewValue += AddNewValue;

            return this;
        }


        public Task<InitChartSourceResponse> Initialize() =>
            Initialize(new SensorHistoryRequest
            {
                To = DateTime.UtcNow,
                Count = -_settings.CustomVisibleCount,
            });

        public Task<InitChartSourceResponse> Initialize(DateTime from, DateTime to) =>
            Initialize(new SensorHistoryRequest
            {
                From = from,
                To = to,
                Count = -TreeValuesCache.MaxHistoryCount,
            });

        public async Task<InitChartSourceResponse> Initialize(SensorHistoryRequest request)
        {
            _newVisibleValues.Clear();

            DataAggregator.RecalculateAggrSections(request);

            var history = await _sensor.GetHistoryData(request);

            foreach (var raw in history.ReverseFluent())
                AddNewValue(raw);

            return new()
            {
                ChartType = DataAggregator.UseAggregation ? AggregatedType : NormalType,
                Values = _newVisibleValues.Select(_filter).ToList(),
            };

        }

        public List<BaseChartValue> InitializeStatic(List<BaseValue> values)
        {
            var versionPoints = new List<BaseChartValue>(values.Capacity);
            foreach (var value in values)
            {
                if (DataAggregator.TryAddNewPoint(value, out var newPoint))
                    versionPoints.Add(newPoint);
            }
            
            return versionPoints;
        }

        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.Select(_filter).ToList(),
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