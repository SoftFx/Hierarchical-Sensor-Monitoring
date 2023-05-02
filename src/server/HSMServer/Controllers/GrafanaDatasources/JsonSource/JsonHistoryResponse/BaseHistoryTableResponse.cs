using HSMServer.Core.Model;
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


    public abstract class BaseHistoryTableResponse : BaseHistoryResponse
    {
        public List<List<object>> Rows { get; } = new();


        public abstract List<ColumnInfo> Columns { get; }

        public string Type { get; } = "table";


        public override BaseHistoryResponse FillRows(List<BaseValue> rawData)
        {
            AddRawValues(rawData);

            return Rows.Count > 0 ? this : null;
        }

        protected abstract void AddRawValues(List<BaseValue> rawData);
    }
}
