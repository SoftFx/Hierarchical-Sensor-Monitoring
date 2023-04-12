using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class ColumnInfo
    {
        public string Text { get; set; }

        public string Type { get; set; } = "string"; //available types "number", "time", "string"


        public ColumnInfo() { }

        public ColumnInfo(string text, string type = "string")
        {
            Text = text;
            Type = type;
        }
    }


    public class HistoryTableResponse : BaseHistoryResponse
    {
        public List<string[]> Rows { get; set; } = new();

        public List<ColumnInfo> Columns { get; set; } = new()
        {
            new("Time", "time"),
            new("Value"),
            new("Status"),
            new("Comment"),
        };


        public string Type { get; set; } = "table";


        public HistoryTableResponse() { }


        public override BaseHistoryResponse FillRows(List<BaseValue> rawData)
        {
            foreach (var raw in rawData)
                AddRow(raw.Time, raw.ShortInfo, raw.Status, raw.Comment);

            return Rows.Count > 0 ? this : null;
        }

        private void AddRow(DateTime time, string value, SensorStatus status, string comment)
        {
            Rows.Add(new string[]
            {
                $"{time.ToUnixMilliseconds()}",
                value,
                $"{status}",
                comment,
            });
        }
    }
}
