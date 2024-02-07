using HSMCommon.Extensions;
using HSMServer.Core.StatisticInfo;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;

namespace HSMServer.Model.ViewModel
{
    public sealed class HistoryStatisticViewModel
    {
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public long TotalValueSize { get; private set; }

        public long TotalKeysSize { get; private set; }

        public long DataCount { get; private set; }

        public string DisplayInfo { get; private set; }


        public double ValuePercent => TotalSize != 0 ? (double)TotalValueSize / TotalSize * 100 : 0.0;

        public long TotalSize => TotalValueSize + TotalKeysSize;

        public bool IsEmpty => LastUpdate == DateTime.MinValue;


        public HistoryStatisticViewModel() { }

        public HistoryStatisticViewModel Update(SensorHistoryInfo historyInfo)
        {
            LastUpdate = DateTime.UtcNow;

            TotalValueSize = historyInfo.ValuesSizeBytes;
            TotalKeysSize = historyInfo.KeysSizeBytes;
            DataCount = historyInfo.DataCount;

            RefreshTotalInfo();

            return this;
        }

        public HistoryStatisticViewModel RecalculateSubTreeStats(ProductNodeViewModel node)
        {
            LastUpdate = DateTime.UtcNow;
            TotalValueSize = 0;
            TotalKeysSize = 0;
            DataCount = 0;

            void Apply(HistoryStatisticViewModel subNodeStat)
            {
                TotalValueSize += subNodeStat.TotalValueSize;
                TotalKeysSize += subNodeStat.TotalKeysSize;
                DataCount += subNodeStat.DataCount;
            }

            foreach (var (_, sensor) in node.Sensors)
                Apply(sensor.HistoryStatistic);

            foreach (var (_, subNode) in node.Nodes)
                Apply(subNode.HistoryStatistic);

            RefreshTotalInfo();

            return this;
        }


        internal string ToCsvFormat(string path) => $"\"{path}\";{DataCount};{TotalSize};{ValuePercent:F4}";

        private void RefreshTotalInfo()
        {
            DisplayInfo = $"{TotalSize.ToReadableMemoryFormat()} ({ValuePercent:F2}% values from {DataCount} records) updated at {LastUpdate.ToDefaultFormat()}";
        }
    }
}