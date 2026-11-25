using System;
using System.Collections.Generic;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>Where a table sensor stands with the server.</summary>
    public enum TableSensorState : byte
    {
        /// <summary>Its registration has not been answered yet. Rows are kept (within the collector's buffer) until it is.</summary>
        Pending = 0,

        /// <summary>The server took its registration: its rows go out with every tick.</summary>
        Registered = 1,

        /// <summary>
        /// The server said no (another schema or another kind of sensor under the path, a server that does not know
        /// table sensors, ...). Final for this collector: rows are dropped and counted.
        /// <see cref="ITableSensor.StateReason"/> carries the server's words.
        /// </summary>
        Refused = 2,

        /// <summary>
        /// This collector cannot send tables: it sends over HTTP, was given a custom <c>DataSender</c>, or uses the
        /// session of another collector. Rows are dropped and counted. The sensor stays unavailable for the life of
        /// the collector, whatever happens to the transport later; create the collector again with a session of its
        /// own. <see cref="ITableSensor.StateReason"/> says why.
        /// </summary>
        Unavailable = 3,
    }


    /// <summary>
    /// A table sensor (ADR-0021): rows of a fixed schema, the time first. Rows are buffered in memory and sent with the
    /// collector's tick, the rows of all table sensors of the collector in one block. The time of a row is the
    /// application's own (for a feed, the provider's time); rows may come out of time order and times may repeat.
    /// <para>
    /// Every member may be called from any thread. Adding a row allocates nothing. An <c>AddRow</c> call returns false
    /// (and <c>AddRows</c> a short count) when rows were not taken: the collector is not running, the sensor is
    /// <see cref="TableSensorState.Refused"/> or <see cref="TableSensorState.Unavailable"/>, or the buffer is full.
    /// Such rows are counted in <see cref="RowsDropped"/> and reported through the collector's diagnostics
    /// (the <c>Queue overflow</c> sensor, queue "Table rows", and <c>Collector errors</c>). A row of the wrong shape (a
    /// cell count other than the column count, a double cell in an integer column) throws
    /// <see cref="ArgumentException"/>: that is a bug of the caller.
    /// </para>
    /// </summary>
    public interface ITableSensor
    {
        /// <summary>The sensor's path as the server stores it (normalized: no empty segments, no spaces around them).</summary>
        string Path { get; }

        /// <summary>The declared value columns, in order; the time column comes before them.</summary>
        IReadOnlyList<TableColumn> Columns { get; }

        /// <summary>Where the sensor stands with the server.</summary>
        TableSensorState State { get; }

        /// <summary>Why the sensor is <see cref="TableSensorState.Refused"/> or <see cref="TableSensorState.Unavailable"/>; null otherwise.</summary>
        string StateReason { get; }

        /// <summary>Rows taken into the buffer since the sensor was created.</summary>
        long RowsAccepted { get; }

        /// <summary>
        /// Rows lost since the sensor was created: refused when added, or dropped later (a full backlog, rows the server
        /// refused, rows still undelivered when the collector stopped).
        /// </summary>
        long RowsDropped { get; }


        /// <summary>Adds a row of one value column.</summary>
        bool AddRow(DateTime time, TableCell first);

        /// <summary>Adds a row of two value columns.</summary>
        bool AddRow(DateTime time, TableCell first, TableCell second);

        /// <summary>Adds a row of three value columns.</summary>
        bool AddRow(DateTime time, TableCell first, TableCell second, TableCell third);

        /// <summary>Adds a row of four value columns.</summary>
        bool AddRow(DateTime time, TableCell first, TableCell second, TableCell third, TableCell fourth);

        /// <summary>Adds a row; <paramref name="cells"/> holds one cell per value column.</summary>
        bool AddRow(DateTime time, TableCell[] cells);

        /// <summary>Adds a row; <paramref name="cells"/> holds one cell per value column.</summary>
        bool AddRow(DateTime time, ReadOnlySpan<TableCell> cells);

        /// <summary>
        /// Adds <paramref name="rowCount"/> rows at once: the time of row i is <c>times[i]</c>, its cells are
        /// <c>cells[i * columns .. (i + 1) * columns)</c> (row-major). Returns the number of rows taken: all of them, or
        /// none (the rows of one call are taken or dropped together).
        /// </summary>
        int AddRows(DateTime[] times, TableCell[] cells, int rowCount);

        /// <summary>
        /// Adds <c>times.Length</c> rows at once; <paramref name="cells"/> holds the cells row-major, one per value
        /// column per row. Returns the number of rows taken: all of them, or none.
        /// </summary>
        int AddRows(ReadOnlySpan<DateTime> times, ReadOnlySpan<TableCell> cells);
    }
}
