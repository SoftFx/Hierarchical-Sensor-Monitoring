namespace HSMSensorDataObjects.SensorRequests
{
    /// <summary>
    /// The type of a table column (#1845). The numbers are those of the table block format
    /// (<c>aicontext/features/api/table-block/feature.md</c>) and are serialized as numbers.
    /// </summary>
    public enum TableColumnType
    {
        Time = 1,
        Int64 = 2,
        Double = 3,
    }


    /// <summary>
    /// One column of a table sensor's schema, in <see cref="AddOrUpdateSensorRequest.TableColumns"/>. Names are compared
    /// ordinally and are not normalized; the rules are those of the table schema (format doc, section "Table schema").
    /// </summary>
    public sealed class TableColumnRequest
    {
        public string Name { get; set; }

        public TableColumnType Type { get; set; }
    }
}
