using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.Extensions;


namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class SimpleHistoryTableResponse : BaseHistoryTableResponse
    {
        private static readonly List<ColumnInfo> _simbleHistoryColumns = new()
        {
            new(nameof(BaseValue.Time), TimeType),
            new(nameof(BooleanValue.Value)),
            new(nameof(BaseValue.Status)),
            new(nameof(BaseValue.Comment)),
        };

        public override List<ColumnInfo> Columns => _simbleHistoryColumns;


        public SimpleHistoryTableResponse() { }


        protected override void AddRawValues(List<BaseValue> rawData)
        {
            foreach (var raw in rawData)
                AddRow(raw.Time, raw.RawValue, raw.Status, raw.Comment);
        }

        private void AddRow(DateTime time, object value, SensorStatus status, string comment)
        {
            Rows.Add(new()
            {
                time.ToUnixMilliseconds(),
                value,
                $"{status}",
                comment,
            });
        }
    }
}
