namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class PayloadOption
    {
        public const string TableDataTypeLabel = "Table";
        public const string PointsDataTypeLabel = "Datapoints";

        public string Label { get; set; }

        public string Value { get; set; }


        public PayloadOption(string value)
        {
            Label = value;
            Value = value;
        }

        public PayloadOption(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}