using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Model.History;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Model.History
{
    public sealed class HistoryTableViewModel : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private readonly BaseSensorModel _model;

        private IAsyncEnumerator<List<BaseValue>> _pagesEnumerator;


        public List<TableValueViewModel> CurrentTablePage => CurrentPage.Select(Build).OrderByDescending(u => u.Time).ToList();

        public List<BaseValue> CurrentPage => Pages.Count > CurrentIndex ? Pages[CurrentIndex] : new();

        public List<List<BaseValue>> Pages { get; } = new();

        public string SensorId { get; }


        public bool IsBarSensor => _model.Type.IsBar();

        public int LastIndex => Pages.Count - 1;


        public int CurrentIndex { get; private set; }


        internal HistoryTableViewModel(BaseSensorModel model)
        {
            _model = model;

            SensorId = model.Id.ToString();
        }


        public async Task Reload(ITreeValuesCache cache, GetSensorHistoryModel request)
        {
            Reset();

            _pagesEnumerator = cache.GetSensorValuesPage(_model.Id, request.FromUtc, request.ToUtc, request.Count).GetAsyncEnumerator(_source.Token);

            await TryReadNextPage();

            LoadCachedValue();

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


        private async Task<bool> TryReadNextPage()
        {
            var hasNext = await _pagesEnumerator.MoveNextAsync();

            if (hasNext && _pagesEnumerator.Current?.Count != 0)
                Pages.Add(_pagesEnumerator.Current);

            return hasNext;
        }

        private void LoadCachedValue()
        {
            if (IsBarSensor && _model is IBarSensor sensor && sensor.LocalLastValue != null)
            {
                var value = sensor.LocalLastValue;

                if (Pages.Count == 0)
                    Pages.Add(new() { value });
                else if (CurrentPage.Count == 0 || CurrentPage.First().Time != value.Time)
                    CurrentPage.Insert(0, value);
            }
        }


        private TableValueViewModel Build(BaseValue value) => _model.Type switch
        {
            SensorType.Boolean => Build((BooleanValue)value),
            SensorType.Integer => Build((IntegerValue)value),
            SensorType.Double => Build((DoubleValue)value),
            SensorType.String => Build((StringValue)value),
            SensorType.IntegerBar => Build((IntegerBarValue)value),
            SensorType.DoubleBar => Build((DoubleBarValue)value),
            SensorType.TimeSpan => Build((TimeSpanValue)value),
            SensorType.File => Build((FileValue)value),
            SensorType.Version => Build((VersionValue)value),
            _ => throw new ArgumentException($"Sensor type {_model.Type} is not allowed for history table"),
        };

        private static SimpleSensorValueViewModel Build<T>(BaseValue<T> value) =>
            new()
            {
                Value = GetTableValue(value),
                Time = value.Time.ToUniversalTime(),
                Status = value.Status.ToClient(),
                Comment = value.Comment,
                ReceivingTime = value.ReceivingTime,
            };

        private static BarSensorValueViewModel Build<T>(BarBaseValue<T> value) where T : struct =>
            new()
            {
                Count = value.Count,
                Min = value.Min.ToString(),
                Max = value.Max.ToString(),
                Mean = value.Mean.ToString(),
                LastValue = value.LastValue.ToString(),
                Time = value.Time.ToUniversalTime(),
                Status = value.Status.ToClient(),
                Comment = value.Comment,
                ReceivingTime = value.ReceivingTime,
            };

        private static string GetTableValue<T>(BaseValue<T> value) => value switch
        {
            VersionValue version => version.Value.RemoveTailZeroes(),
            TimeSpanValue timespan => timespan.Value.ToTableView(),
            _ => value.Value?.ToString(),
        };
    }
}