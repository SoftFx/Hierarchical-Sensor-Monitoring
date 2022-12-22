using HSMServer.Core.Model;
using HSMServer.Pagination;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.History
{
    public sealed class HistoryValuesViewModel
    {
        public string EncodedId { get; }

        public SensorType SensorType { get; }

        public int CurrentPageIndex { get; }

        public bool NextPageIsEnabled { get; }

        public bool PrevPageIsEnabled { get; }

        public string OldestValueTime { get; }

        public List<HistoryValueViewModel> Values { get; }

        public bool IsBarSensor => SensorType is SensorType.IntegerBar or SensorType.DoubleBar;


        internal HistoryValuesViewModel(string encodedId, int type, ISensorValuesHistoryPagination pagination)
        {
            EncodedId = encodedId;
            SensorType = (SensorType)type;
            CurrentPageIndex = pagination.CurrentPageIndex;
            NextPageIsEnabled = pagination.HasNextPage;
            PrevPageIsEnabled = pagination.HasPrevPage;

            var values = pagination.CurrentPage;

            OldestValueTime = values.LastOrDefault()?.Time.ToUniversalTime().ToString("O") ?? string.Empty;
            Values = values.Select(v => HistoryValueViewModel.Create(v, type)).ToList();
        }
    }
}
