using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Core.Model.Authentication.History
{
    public sealed class HistoryValuesViewModel
    {
        private readonly IAsyncEnumerator<List<BaseValue>> _pagesEnumerator;


        public List<List<BaseValue>> Pages { get; } = new();

        public string EncodedId { get; }

        public SensorType SensorType { get; }

        public bool IsBarSensor => SensorType is SensorType.IntegerBar or SensorType.DoubleBar;

        public int LastPageIndex => Pages.Count - 1;

        public int CurrentPageIndex { get; private set; }


        public HistoryValuesViewModel(string encodedId, int type, IAsyncEnumerable<List<BaseValue>> enumerator)
        {
            _pagesEnumerator = enumerator.GetAsyncEnumerator();

            EncodedId = encodedId;
            SensorType = (SensorType)type;
        }


        public async Task<HistoryValuesViewModel> Initialize()
        {
            await TryReadNextPage();
            await TryReadNextPage();

            return this;
        }

        public async Task<HistoryValuesViewModel> ToNextPage()
        {
            if (++CurrentPageIndex == LastPageIndex)
                if (!await TryReadNextPage())
                    CurrentPageIndex = Math.Min(CurrentPageIndex, LastPageIndex);

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

            if (hasNext)
                Pages.Add(_pagesEnumerator.Current);

            return hasNext;
        }
    }
}
