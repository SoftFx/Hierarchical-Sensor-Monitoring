using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class BarHistoryTableResponse : BaseHistoryTableResponse
    {
        private static readonly List<ColumnInfo> _barHistoryColumns = new()
        {
            new(nameof(BaseValue.Time), "time"),
            new(nameof(IntegerBarValue.OpenTime), "time"),
            new(nameof(IntegerBarValue.CloseTime), "time"),
            new(nameof(IntegerBarValue.Min), "number"),
            new(nameof(IntegerBarValue.Max), "number"),
            new(nameof(IntegerBarValue.Mean), "number"),
            new(nameof(IntegerBarValue.Count), "number"),
            new(nameof(IntegerBarValue.LastValue), "number"),
            new(nameof(BaseValue.Status)),
            new(nameof(BaseValue.Comment)),
        };

        public override List<ColumnInfo> Columns => _barHistoryColumns;


        public BarHistoryTableResponse() { }


        protected override void AddRawValues(List<BaseValue> rawData)
        {
            foreach (var raw in rawData)
                if (raw is BarBaseValue bar)
                {
                    var historyRow = new List<object>
                    {
                        bar.Time.ToUnixMilliseconds(),
                        bar.OpenTime.ToUnixMilliseconds(),
                        bar.CloseTime.ToUnixMilliseconds(),
                    };

                    if (bar is IntegerBarValue intBar)
                    {
                        historyRow.AddFluent(intBar.Min)
                                  .AddFluent(intBar.Max)
                                  .AddFluent(intBar.Mean)
                                  .AddFluent(intBar.Count)
                                  .AddFluent(intBar.LastValue);
                    }
                    else if (bar is DoubleBarValue doubleBar)
                    {
                        historyRow.AddFluent(doubleBar.Min)
                                  .AddFluent(doubleBar.Max)
                                  .AddFluent(doubleBar.Mean)
                                  .AddFluent(doubleBar.Count)
                                  .AddFluent(doubleBar.LastValue);
                    }

                    Rows.Add(historyRow.AddFluent($"{bar.Status}")
                                       .AddFluent(bar.Comment));
                }
        }
    }
}
