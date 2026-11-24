using System;
using HSMServer.Core.TableSensors;
using HSMTableBlocks;

namespace HSMServer.ServerConfiguration
{
    // Table sensors (ADR-0021, #1844): the limits a block is checked against on a put, the most rows a read returns, and
    // the free disk space below which puts are refused. Read once at startup and validated with an error that names the
    // key. Nothing here starts any work: a server without table sensors opens nothing.
    public sealed class TableSensorsConfig
    {
        public int MaxDecompressedBytes { get; set; } = TableBlockLimits.DefaultMaxDecompressedBytes;

        public int MaxTables { get; set; } = TableBlockLimits.DefaultMaxTables;

        public int MaxRows { get; set; } = TableBlockLimits.DefaultMaxRows;

        public int MaxColumnsPerTable { get; set; } = TableBlockLimits.DefaultMaxColumnsPerTable;

        public int MaxTableIdLength { get; set; } = TableBlockLimits.DefaultMaxTableIdLength;

        // Ten million rows of five columns would be 400 MB in one request; the default keeps a read near 40 MB.
        public int MaxReadRows { get; set; } = TableBlockOptions.DefaultMaxReadRows;

        // Puts are refused (a fault, the collector keeps its blocks) while less is free on the store's disk; 0 turns it off.
        public long MinFreeDiskMb { get; set; } = TableBlockOptions.DefaultMinFreeDiskMb;


        public void Validate() => _ = ToOptions();

        public TableBlockOptions ToOptions()
        {
            Check(MaxDecompressedBytes, TableBlockLimits.FormatMaxDecompressedBytes, nameof(MaxDecompressedBytes));
            Check(MaxTables, int.MaxValue, nameof(MaxTables));
            Check(MaxRows, int.MaxValue, nameof(MaxRows));
            Check(MaxColumnsPerTable, TableBlockLimits.FormatMaxColumnsPerTable, nameof(MaxColumnsPerTable));
            Check(MaxTableIdLength, TableBlockLimits.FormatMaxTableIdLength, nameof(MaxTableIdLength));
            Check(MaxReadRows, int.MaxValue, nameof(MaxReadRows));

            if (MinFreeDiskMb < 0)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{nameof(MinFreeDiskMb)} = {MinFreeDiskMb} is invalid: expected 0 (no check) or more megabytes.");

            return new TableBlockOptions
            {
                Limits = new TableBlockLimits(MaxDecompressedBytes, MaxTables, MaxRows, MaxColumnsPerTable, MaxTableIdLength),
                MaxReadRows = MaxReadRows,
                MinFreeDiskMb = MinFreeDiskMb,
            };
        }

        private static void Check(long value, long max, string name)
        {
            if (value < 1 || value > max)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{name} = {value} is invalid: expected 1..{max}.");
        }
    }
}
