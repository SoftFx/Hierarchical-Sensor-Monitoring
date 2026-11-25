using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>
    /// One value column of a table sensor's schema: a name and a type. The time of a row is not a column you declare:
    /// every table sensor has it implicitly, first, named <see cref="TimeColumnName"/>.
    /// </summary>
    public readonly struct TableColumn
    {
        /// <summary>The name of the time column every table sensor has first. No declared column may take it.</summary>
        public const string TimeColumnName = "time";


        private TableColumn(string name, TableColumnType type)
        {
            Name = name;
            Type = type;
        }


        /// <summary>The name of the column; compared ordinally, as the server compares it.</summary>
        public string Name { get; }

        /// <summary><see cref="TableColumnType.Double"/> or <see cref="TableColumnType.Int64"/>.</summary>
        public TableColumnType Type { get; }


        /// <summary>A column of doubles.</summary>
        public static TableColumn Double(string name) => new TableColumn(name, TableColumnType.Double);

        /// <summary>A column of 64-bit integers.</summary>
        public static TableColumn Int64(string name) => new TableColumn(name, TableColumnType.Int64);


        public override string ToString() => $"{Name}:{Type}";
    }
}
