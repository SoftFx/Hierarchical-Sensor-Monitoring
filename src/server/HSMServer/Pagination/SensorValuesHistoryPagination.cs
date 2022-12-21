using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Pagination
{
    public sealed class SensorValuesHistoryPagination : ISensorValuesHistoryPagination
    {
        private const int MaxHistoryCount = -TreeValuesCache.MaxHistoryCount;

        private readonly ITreeValuesCache _cache;
        private readonly List<List<BaseValue>> _allPages = new();

        private IAsyncEnumerator<List<BaseValue>> _pagesEnumerator;
        private List<BaseValue> _nextPage;


        public List<BaseValue> CurrentPage { get; private set; }

        public int CurrentPageIndex { get; private set; }

        public bool HasNextPage => (_nextPage?.Count ?? 0) != 0;

        public bool HasPrevPage => CurrentPageIndex > 1;


        public SensorValuesHistoryPagination(ITreeValuesCache cache)
        {
            _cache = cache;
        }


        public async Task InitializeEnumerator(string encodedId, DateTime from, DateTime to)
        {
            _pagesEnumerator = _cache.GetSensorValuesPage(SensorPathHelper.DecodeGuid(encodedId), from.ToUniversalTime(), to.ToUniversalTime(), MaxHistoryCount)
                                     .GetAsyncEnumerator();

            await BuildFirstPage();
            await BuildNextPage();
        }

        public async Task SwitchToNextPage()
        {
            CurrentPage = _nextPage;
            CurrentPageIndex++;

            await BuildNextPage();
        }

        public void SwitchToPrevPage()
        {
            _nextPage = CurrentPage;

            CurrentPage = _allPages[CurrentPageIndex - 2];
            CurrentPageIndex--;
        }

        private async Task BuildFirstPage()
        {
            _allPages.Clear();

            await _pagesEnumerator.MoveNextAsync();

            CurrentPage = _pagesEnumerator.Current;
            CurrentPageIndex = 1;

            _allPages.Add(CurrentPage);
        }

        private async Task BuildNextPage()
        {
            if (_allPages.Count - 1 >= CurrentPageIndex)
            {
                _nextPage = _allPages[CurrentPageIndex];

                return;
            }

            await _pagesEnumerator.MoveNextAsync();

            _nextPage = _pagesEnumerator.Current;

            if (HasNextPage && _allPages.Count == CurrentPageIndex)
                _allPages.Add(_nextPage);
        }
    }
}
