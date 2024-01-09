using HSMCommon.Extensions;
using HSMServer.Core.StatisticInfo;
using System;
using System.Drawing;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorHistoryStatisticViewModel
    {
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public string KeyValueBalance { get; private set; }

        public string TotalSize { get; private set; }


        public long DataCount { get; private set; }


        public bool IsEmpty => LastUpdate == DateTime.MinValue;

        public string TotalInfo { get; private set; }


        public SensorHistoryStatisticViewModel() { }

        public SensorHistoryStatisticViewModel Update(SensorHistoryInfo historyInfo)
        {
            LastUpdate = DateTime.UtcNow;

            KeyValueBalance = $"{(double)historyInfo.ValuesSizeBytes / historyInfo.TotalSizeBytes * 100:F2}% values";
            TotalSize = historyInfo.TotalSizeBytes.ToReadableMemoryFormat();
            DataCount = historyInfo.DataCount;

            TotalInfo = $"Count - {DataCount}, Size - {TotalSize} ({KeyValueBalance})";

            return this;
        }
    }
}