using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    /// <summary>
    /// What a table sensor (ADR-0021, #1844) adds to its <see cref="SensorEntity"/>: the schema fixed at registration
    /// and the first block sequence number its rows may come from. Null on every other sensor.
    /// </summary>
    public sealed record TableSensorEntity
    {
        /// <summary>The schema id (CRC-32 of the canonical schema), stored to detect a change of the canonical form.</summary>
        public uint SchemaId { get; init; }

        /// <summary>The columns in order; the first is the time.</summary>
        public List<TableColumnEntity> Columns { get; init; }

        /// <summary>
        /// The sequence number of the first block that may hold this sensor's rows: blocks stored before it was created
        /// belong to an earlier sensor of the same path and are never read for it.
        /// </summary>
        public long FirstSequence { get; init; }
    }


    /// <summary>One column of a table sensor's schema.</summary>
    public sealed record TableColumnEntity
    {
        public string Name { get; init; }

        /// <summary>The column type as the block format numbers it: 1 time, 2 int64, 3 double.</summary>
        public byte Type { get; init; }
    }
}
