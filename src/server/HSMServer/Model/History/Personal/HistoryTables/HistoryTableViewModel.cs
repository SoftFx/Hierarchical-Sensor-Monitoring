using HSMCommon.Extensions;
using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Model.History;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Model.Sensors;

namespace HSMServer.Model.History
{
    public sealed class HistoryTableViewModel : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private readonly BaseSensorModel _model;

        private IAsyncEnumerator<List<BaseValue>> _pagesEnumerator;


        public TableSettingsModel TableSettings => _model.TableSettings;
        
        public List<TableValueViewModel> CurrentTablePage
        {
            get
            {
                DateTime ByReceivingTime(TableValueViewModel value) => value.ReceivingTime;

                DateTime ByTime(TableValueViewModel value) => value.Time;


                Func<TableValueViewModel, DateTime> orderByFilter = _model.AggregateValues ? ByReceivingTime : ByTime;
                Func<TableValueViewModel, DateTime> thenByFilter = _model.AggregateValues ? ByTime : ByReceivingTime;

                return CurrentPage.Select(Build).OrderByDescending(orderByFilter).ThenByDescending(thenByFilter).ToList();
            }
        }

        public List<BaseValue> CurrentPage => Pages.Count > CurrentIndex ? Pages[CurrentIndex] : new();

        public List<List<BaseValue>> Pages { get; } = new();

        public string SensorId { get; }


        public bool AggregateValues => _model.AggregateValues;

        public bool IsEma => _model.Statistics.HasEma();

        public bool IsBarSensor => _model.Type.IsBar();

        public bool IsAliveSensor => string.Equals(_model.DisplayName, _serviceAliveSensorName);

        public int LastIndex => Pages.Count - 1;


        public int CurrentIndex { get; private set; }

        public SensorType SensorType => _model.Type;


        internal HistoryTableViewModel(BaseSensorModel model)
        {
            _model = model;

            SensorId = model.Id.ToString();
        }


        public async Task Reload(ITreeValuesCache cache, GetSensorHistoryRequest request)
        {
            Reset();

            _pagesEnumerator = cache.GetSensorValuesPage(_model.Id, request.FromUtc, request.ToUtc, request.Count, request.Options).GetAsyncEnumerator(_source.Token);

            await TryReadNextPage();

            LoadCachedValue(request.FromUtc, request.ToUtc);

            await TryReadNextPage();
        }

        public async Task<HistoryTableViewModel> ToNextPage()
        {
            await TryReadNextPage();

            CurrentIndex = Math.Min(CurrentIndex + 1, LastIndex);

            return this;
        }

        public HistoryTableViewModel ToPreviousPage()
        {
            CurrentIndex = Math.Max(CurrentIndex - 1, 0);

            return this;
        }

        public void Dispose() => _source.Cancel();

        public void Reset()
        {
            CurrentIndex = 0;
            Pages.Clear();
        }



        public async Task<List<BaseValue>> GetAllValues()
        {
            while (await TryReadNextPage())
            {  }

            List<BaseValue> result = new List<BaseValue>();
            Pages.ForEach(result.AddRange);

            return result;
        }

        private async Task<bool> TryReadNextPage()
        {
            var hasNext = await _pagesEnumerator.MoveNextAsync();

            if (hasNext && _pagesEnumerator.Current?.Count != 0)
                Pages.Add(_pagesEnumerator.Current);

            return hasNext;
        }

        private void LoadCachedValue(DateTime from, DateTime to)
        {
            if (IsBarSensor && _model is IBarSensor sensor && sensor.LocalLastValue != null)
            {
                var value = sensor.LocalLastValue;

                if (from <= value.Time && value.Time <= to)
                {
                    if (Pages.Count == 0)
                        Pages.Add(new() { value });
                    else if (CurrentPage.Count == 0 || CurrentPage.First().Time != value.Time)
                        CurrentPage.Insert(0, value);
                }
            }
        }


        private TableValueViewModel Build(BaseValue value) => _model.Type switch
        {
            //SensorType.Boolean => Build((BooleanValue)value),
            SensorType.Boolean => IsAliveSensor ? BuildIsAliveSensorValues((BooleanValue)value) : Build((BooleanValue)value),
            SensorType.Integer => Build((IntegerValue)value),
            SensorType.Double => Build((DoubleValue)value),
            SensorType.Rate => Build((RateValue)value),
            SensorType.String => Build((StringValue)value),
            SensorType.IntegerBar => Build((IntegerBarValue)value),
            SensorType.DoubleBar => Build((DoubleBarValue)value),
            SensorType.TimeSpan => Build((TimeSpanValue)value),
            SensorType.File => Build((FileValue)value),
            SensorType.Version => Build((VersionValue)value),
            SensorType.Enum => Build((EnumValue)value),
            _ => throw new ArgumentException($"Sensor type {_model.Type} is not allowed for history table"),
        };

        private const string _serviceAliveSensorName = "Service alive";

       

        private SimpleSensorValueViewModel Build<T>(BaseValue<T> value) =>
            new()
            {
                Value = GetTableValue(value),
                EmaValue = value.EmaValue?.ToString(),
                Time = value.Time.ToUniversalTime(),
                Status = value.Status.ToClient(),
                Comment = value.Comment,
                ReceivingTime = value.ReceivingTime,
                LastUpdateTime = value.LastUpdateTime,
                AggregatedValuesCount = value.AggregatedValuesCount,
                IsTimeout = value.IsTimeout
            };

        private BarSensorValueViewModel Build<T>(BarBaseValue<T> value) where T : struct, INumber<T> =>
            new()
            {
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Count = value.Count,
                Min = value.Min.ToString(),
                Max = value.Max.ToString(),
                Mean = value.Mean.ToString(),
                EmaMin = value.EmaMin?.ToString(),
                EmaMax = value.EmaMax?.ToString(),
                EmaMean = value.EmaMean?.ToString(),
                EmaCount = value.EmaCount?.ToString(),
                FirstValue = value.FirstValue?.ToString(),
                LastValue = value.LastValue.ToString(),
                Time = value.Time.ToUniversalTime(),
                Status = value.Status.ToClient(),
                Comment = value.Comment,
                ReceivingTime = value.ReceivingTime,
                IsTimeout = value.IsTimeout
            };

        private SimpleSensorValueViewModel BuildIsAliveSensorValues(BaseValue<bool> value) =>
            new()
            {
                Value = value.Value ? "Running" : "Restarted",
                EmaValue = value.EmaValue?.ToString(),
                Time = value.Time.ToUniversalTime(),
                Status = value.Status.ToClient(),
                Comment = BuildIsAliveSensorComment(value),
                ReceivingTime = value.ReceivingTime,
                LastUpdateTime = value.LastUpdateTime,
                AggregatedValuesCount = value.AggregatedValuesCount,
                IsTimeout = value.IsTimeout
            };

        private string BuildIsAliveSensorComment(BaseValue<bool> value)
        {
            if (!string.IsNullOrEmpty(value.Comment))
                return value.Comment;

            return value.Value ? "Service is working" : "Service has been restarted";
        }

        private string GetTableValue<T>(BaseValue<T> value) => value switch
        {
            VersionValue version => version.Value?.RemoveTailZeroes() ?? string.Empty,
            TimeSpanValue timespan => timespan.Value.ToReadableView(),
            RateValue rate => Math.Round(rate.Value, 5).ToString(CultureInfo.InvariantCulture),
            EnumValue v => v.Value != null && _model.EnumOptions.TryGetValue(v.Value, out var option) ? option.Value : v.Value.ToString(),
            _ => value.Value?.ToString() ?? string.Empty,
        };

    }
}