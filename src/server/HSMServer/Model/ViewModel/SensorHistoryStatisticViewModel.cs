using HSMCommon.Extensions;
using HSMServer.Core.StatisticInfo;
using System;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorHistoryStatisticViewModel
    {
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public string KeyValueBalance { get; private set; }

        public string TotalSize { get; private set; }


        public long DataCount { get; private set; }


        public bool IsEmpty => LastUpdate == DateTime.MinValue;


        public SensorHistoryStatisticViewModel() { }

        public SensorHistoryStatisticViewModel Update(SensorHistoryInfo historyInfo)
        {
            LastUpdate = DateTime.UtcNow;

            KeyValueBalance = $"{historyInfo.ValuesSizeBytes / historyInfo.TotalSizeBytes * 100}% values";
            TotalSize = historyInfo.TotalSizeBytes.ToReadableMemoryFormat();
            DataCount = historyInfo.DataCount;

            return this;
        }
    }
}