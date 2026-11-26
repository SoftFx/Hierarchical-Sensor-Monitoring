using System;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    /// <summary>
    /// Optional collector capability: table sensors (ADR-0021). Kept apart from <see cref="IDataCollector"/> so external
    /// implementations of that interface stay source- and binary-compatible; <see cref="DataCollector"/> implements it.
    /// </summary>
    public interface ITableSensorCollector
    {
        /// <summary>
        /// Creates a table sensor, or returns the one already created under the same path with the same columns.
        /// <para>
        /// Throws <see cref="ArgumentException"/> (or <see cref="ArgumentNullException"/>) only for a mistake of the call:
        /// a null or empty path, a path with a backslash or a tab, a path longer than 1,024 UTF-8 bytes in its
        /// normalized form, no columns, an invalid or duplicate column name, a column named
        /// <see cref="TableColumn.TimeColumnName"/>, or a path already created with other columns.
        /// </para>
        /// <para>
        /// Never throws for a condition of the environment: a collector that cannot send tables (HTTP transport, a custom
        /// <c>DataSender</c>, the session of another collector) or one that is stopping returns a sensor whose
        /// <see cref="ITableSensor.State"/> is <see cref="TableSensorState.Unavailable"/>, says why once through the
        /// collector's errors, and counts the rows it is given as dropped.
        /// </para>
        /// </summary>
        ITableSensor CreateTableSensor(string path, TableSensorOptions options);
    }


    public static class TableSensorExtensions
    {
        /// <summary>
        /// Creates a table sensor on a collector that has the <see cref="ITableSensorCollector"/> capability (every
        /// <see cref="DataCollector"/>). See <see cref="ITableSensorCollector.CreateTableSensor"/>.
        /// </summary>
        /// <exception cref="NotSupportedException"><paramref name="collector"/> is another implementation of <see cref="IDataCollector"/>.</exception>
        public static ITableSensor CreateTableSensor(this IDataCollector collector, string path, TableSensorOptions options)
        {
            if (collector == null)
                throw new ArgumentNullException(nameof(collector));

            if (collector is ITableSensorCollector tables)
                return tables.CreateTableSensor(path, options);

            throw new NotSupportedException($"{collector.GetType().FullName} does not implement {nameof(ITableSensorCollector)}: table sensors need the DataCollector of this package.");
        }
    }
}
