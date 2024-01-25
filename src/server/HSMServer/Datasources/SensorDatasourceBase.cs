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
        //private readonly CLinkedList<BaseChartValue> _curVisibleValues = new();

        private SourceSettings _settings;
        private BaseSensorModel _sensor;

        //private long _removedValuesCnt;//, _aggrValuesStep;
        //private bool _aggreagateValues;

        //protected BarBaseValue _lastBarValue; //TODO move to special derived class
        //protected bool _isBarSensor;



        protected abstract BaseDataAggregator DataAggregator { get; }

        protected abstract ChartType AggregatedType { get; }

        protected abstract ChartType NormalType { get; }


        //protected abstract void AddVisibleValue(BaseValue baseValue);

        protected abstract void ApplyToLast(BaseValue newValue);


        internal virtual SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            _settings = settings;
            _sensor = sensor;

            DataAggregator.Setup(settings);
            //_isBarSensor = sensor.Type.IsBar();

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
            DataAggregator.RecalculateStep(request);

            _newVisibleValues.Clear();
            //_curVisibleValues.Clear();
            //_removedValuesCnt = 0;

            var history = await _sensor.GetHistoryData(request);

            history.Reverse();

            foreach (var raw in history)
                AddNewValue(raw);

            //if (history is not null)
            //{
            //    var data = await history;

            //    BuildInitialValues(data, request);

            //}

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

                //RemovedValuesCount = _removedValuesCnt, //not use?
                IsTimeSpan = _sensor.Type is SensorType.TimeSpan
            };


        public void Dispose()
        {
            _sensor.ReceivedNewValue -= AddNewValue;
        }


        //protected bool IsPartialValueUpdate(BaseValue newValue)
        //{
        //    return _isBarSensor && newValue is BarBaseValue barValue &&
        //           _lastBarValue?.OpenTime == barValue?.OpenTime &&
        //           _lastBarValue.CloseTime == barValue?.CloseTime;
        //}

        protected void AddVisibleToLast(BaseChartValue value)
        {
            //_curVisibleValues.AddLast(value);
            _newVisibleValues.AddLast(value);
        }

        //private void BuildInitialValues(List<BaseValue> rawList, SensorHistoryRequest request)
        //{
        //    rawList.Reverse();

        //    //_aggreagateValues = rawList.Count > MaxVisibleCnt;
        //    //_aggrValuesStep = _aggreagateValues ? (request.To - request.From).Ticks / MaxVisibleCnt : 0L;
        //    _curVisibleValues.Clear();

        //    foreach (var raw in rawList)
        //        AddNewValue(raw);
        //}

        private void AddNewValue(BaseValue value)
        {
            if (!DataAggregator.TryAddNewPoint(value, out var newPoint) && _newVisibleValues.Count > 0)
                return;

            _newVisibleValues.AddLast(newPoint); //new values of first recalculate

            while (_newVisibleValues.Count > _settings.MaxVisibleCount)
                _newVisibleValues.RemoveFirst();

            //if (DataAggregator.UseCustomApply(value))
            //    ApplyToLast(value);
            //else if (_aggreagateValues && _aggrValuesStep > 0)
            //{
            //    if (_curVisibleValues.Count == 0 || _curVisibleValues.Last.Value.Time.Ticks + _aggrValuesStep < value.Time.Ticks)
            //        AddVisibleValue(value);
            //    else
            //        ApplyToLast(value);
            //}
            //else
            //    AddVisibleValue(value);

            //while (_curVisibleValues.Count > MaxVisibleCnt)
            //{
            //    _curVisibleValues.RemoveFirst();
            //    _removedValuesCnt = Math.Min(++_removedValuesCnt, MaxVisibleCnt);
            //}

            //while (_newVisibleValues.Count > MaxVisibleCnt)
            //    _newVisibleValues.RemoveFirst();
        }
    }
}