using HSMCommon.Extensions;
using HSMServer.Core.StatisticInfo;
using System;

namespace HSMServer.Model.ViewModel
{



    public class SensorHistoryStatisticViewModel
    {
        public DateTime LastUpdate { get; private set; } = DateTime.MinValue;

        public long Size { get; private set; }

        public double Percent { get; private set; }


        public string KeyValueBalance { get; private set; }

        public string TotalSize { get; private set; }


        public long DataCount { get; private set; }


        public bool IsEmpty => LastUpdate == DateTime.MinValue;

        public string TotalInfo { get; private set; }


        public SensorHistoryStatisticViewModel() { }

        public SensorHistoryStatisticViewModel Update(SensorHistoryInfo historyInfo)
        {
            LastUpdate = DateTime.UtcNow;

            Percent = (double)historyInfo.ValuesSizeBytes / historyInfo.TotalSizeBytes * 100;
            Size = historyInfo.TotalSizeBytes;

            KeyValueBalance = $"{Percent:F2}% values";
            TotalSize = Size.ToReadableMemoryFormat();
            DataCount = historyInfo.DataCount;

            TotalInfo = $"Count - {DataCount}, Size - {TotalSize} ({KeyValueBalance})";

            return this;
        }

        public SensorHistoryStatisticViewModel Update(NodeHistoryInfo historyInfo)
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

            Percent = (double)totalValuesSizeCount / (totalKeysSizeCount + totalValuesSizeCount) * 100;
            Size = totalKeysSizeCount + totalValuesSizeCount;

            KeyValueBalance = $"{Percent:F2}% values";
            TotalSize = (totalKeysSizeCount + totalValuesSizeCount).ToReadableMemoryFormat();
            DataCount = totalData;

            TotalInfo = $"Count - {DataCount}, Size - {TotalSize} ({KeyValueBalance})";

            return this;
        }
    }
}