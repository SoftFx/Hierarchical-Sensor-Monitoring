using HSMCommon.Extensions;
using HSMServer.Core.StatisticInfo;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.ViewModel
{
    public sealed class HistoryStatisticViewModel
    {
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public double Percent { get; private set; }

        public long TotalSize { get; private set; }

        public long DataCount { get; private set; }


        public bool IsEmpty => LastUpdate == DateTime.MinValue;

        public string TotalInfo { get; private set; }


        public HistoryStatisticViewModel() { }

        public HistoryStatisticViewModel Update(SensorHistoryInfo historyInfo)
        {
            LastUpdate = DateTime.UtcNow;

            TotalSize = historyInfo.TotalSizeBytes;
            Percent = (double)historyInfo.ValuesSizeBytes / TotalSize * 100;

            DataCount = historyInfo.DataCount;

            RefreshTotalInfo();

            return this;
        }

        public HistoryStatisticViewModel Update(NodeHistoryInfo historyInfo)
        {
            var totalValuesSizeCount = 0L;
            var totalKeysSizeCount = 0L;
            var totalData = 0L;

            void CalculateTotal(NodeHistoryInfo info)
            {
                foreach (var (_, subNodeInfo) in info.SubnodesInfo)
                    CalculateTotal(subNodeInfo);

                foreach (var (_, sensor) in info.SensorsInfo)
                {
                    totalValuesSizeCount += sensor.ValuesSizeBytes;
                    totalKeysSizeCount += sensor.KeysSizeBytes;

                    totalData += sensor.DataCount;
                }
            }

            LastUpdate = DateTime.UtcNow;

            CalculateTotal(historyInfo);

            DataCount = totalData;
            TotalSize = totalKeysSizeCount + totalValuesSizeCount;
            Percent = (double)totalValuesSizeCount / TotalSize * 100;

            RefreshTotalInfo();

            return this;
        }


        internal string ToCsvFormat(string path) => $"\"{path}\";{DataCount};{TotalSize};{Percent:F4}";

        private void RefreshTotalInfo()
        {
            TotalInfo = $"{TotalSize.ToReadableMemoryFormat()} ({Percent:F2}% values from {DataCount} records) updated at {LastUpdate.ToDefaultFormat()}";
        }
    }
}