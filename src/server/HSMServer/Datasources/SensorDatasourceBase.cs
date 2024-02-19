using HSMCommon.Collections;
using HSMCommon.Extensions;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Datasources.Aggregators;
using System;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Dashboards;

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
        private PanelSettings _panelSettings;
        private BaseSensorModel _sensor;


        protected abstract BaseDataAggregator DataAggregator { get; }

        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        protected internal Func<BaseChartValue, object> Filter;
        

        internal virtual SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings, PanelSettings panelSettings)
        {
            _settings = settings;
            _sensor = sensor;
            _panelSettings = panelSettings;
            
            Filter = panelSettings?.AutoScale ?? true ? value => value : value => value.Filter(_panelSettings);
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
                Values = _newVisibleValues.Select(Filter).ToList(),
            };
            
        }

        public UpdateChartSourceResponse GetSourceUpdates() =>
            new()
            {
                NewVisibleValues = _newVisibleValues.Select(Filter).ToList(),
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