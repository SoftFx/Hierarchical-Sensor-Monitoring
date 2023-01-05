using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Model.Authentication.History
{
    public sealed class HistoryValuesViewModel
    {
        private readonly IAsyncEnumerator<List<BaseValue>> _pagesEnumerator;

        private readonly BarBaseValue _localLastValue;


        public List<List<BaseValue>> Pages { get; } = new();

        public string EncodedId { get; }

        public SensorType SensorType { get; }

        public bool IsBarSensor => SensorType is SensorType.IntegerBar or SensorType.DoubleBar;

        public int LastPageIndex => Pages.Count - 1;

        public int CurrentPageIndex { get; private set; }


        public HistoryValuesViewModel(string encodedId, int type, IAsyncEnumerable<List<BaseValue>> enumerator, BarBaseValue localLastValue = null)
        {
            _pagesEnumerator = enumerator.GetAsyncEnumerator();
            _localLastValue = localLastValue;

            EncodedId = encodedId;
            SensorType = (SensorType)type;
        }


        public async Task<HistoryValuesViewModel> Initialize()
        {
            await TryReadNextPage();

            if (_localLastValue is not null)
            {
                if (Pages.Count == 0)
                    Pages.Add(new() { _localLastValue });
                else Pages[0].Insert(0, _localLastValue);
            }

            await TryReadNextPage();

            return this;
        }

        public async Task<HistoryValuesViewModel> ToNextPage()
        {
            await TryReadNextPage();

            CurrentPageIndex = Math.Min(CurrentPageIndex + 1, LastPageIndex);

            return this;
        }

        public HistoryValuesViewModel ToPreviousPage()
        {
            CurrentPageIndex = Math.Max(CurrentPageIndex - 1, 0);

            return this;
        }

        private async Task<bool> TryReadNextPage()
        {
            var hasNext = await _pagesEnumerator.MoveNextAsync();

            if (hasNext && _pagesEnumerator.Current?.Count != 0)
                Pages.Add(_pagesEnumerator.Current);

            return hasNext;
        }
    }
}