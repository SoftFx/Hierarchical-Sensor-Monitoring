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

        // How many blocks the table connections store at once (#1845). A put decompresses its block (up to
        // MaxDecompressedBytes) and makes a synced write; a connection whose block waits for a turn is not answered until
        // it got one, which is the collector's back-pressure (one block in flight per connection).
        public int MaxConcurrentPuts { get; set; } = DefaultMaxConcurrentPuts;

        public const int DefaultMaxConcurrentPuts = 4;

        // The most rows one CSV or JSON answer of the read route holds (#1845): text is several times the size of the
        // rows, and a client without the library reads it whole. The blocks answer is capped by MaxReadRows.
        public int MaxTextReadRows { get; set; } = DefaultMaxTextReadRows;

        public const int DefaultMaxTextReadRows = 100_000;

        // How many reads of the read route run at once (#1845); a read of rows not merged yet decompresses every block of
        // its range. A request beyond them waits for a turn.
        public int MaxConcurrentReads { get; set; } = DefaultMaxConcurrentReads;

        public const int DefaultMaxConcurrentReads = 2;

        // The most week databases of the batch store open at once (#1850): a read of a long range opens and closes them in turn.
        public int MaxOpenBatchDatabases { get; set; } = TableBlockOptions.DefaultMaxOpenBatchDatabases;

        // How long the blocks of a merged hour are kept after the hour is committed (#1850); 0 deletes them at once.
        public int DeleteMergedBlocksAfterHours { get; set; } = TableBlockOptions.DefaultDeleteMergedBlocksAfterHours;

        // The memory the merge of one product's hour may take for its rows (#1850); a larger hour is merged in several passes.
        public int MergeMemoryBudgetMb { get; set; } = TableBlockOptions.DefaultMergeMemoryBudgetMb;

        // The period of the feed sensors of a product (#1847): .tables/Rows, Stored, Delivery lag, Tables refused, Blocks the
        // merge could not read, each the count (or the maximum) of one period.
        public int FeedProductPeriodMinutes { get; set; } = DefaultFeedProductPeriodMinutes;

        public const int DefaultFeedProductPeriodMinutes = 1;

        // The period of the Rows sensor of each table sensor (#1847), .tables/<the table's path>/Rows: one value per active
        // table and period, the cost that grows with the tables. A whole number of product periods.
        public int FeedTablePeriodMinutes { get; set; } = DefaultFeedTablePeriodMinutes;

        public const int DefaultFeedTablePeriodMinutes = 5;

        // False: no Rows sensor per table sensor is created or posted (the product's sensors still are).
        public bool FeedTableSensors { get; set; } = true;


        public void Validate() => _ = ToOptions();

        public TableBlockOptions ToOptions()
        {
            Check(MaxDecompressedBytes, TableBlockLimits.FormatMaxDecompressedBytes, nameof(MaxDecompressedBytes));
            Check(MaxTables, int.MaxValue, nameof(MaxTables));
            Check(MaxRows, int.MaxValue, nameof(MaxRows));
            Check(MaxColumnsPerTable, TableBlockLimits.FormatMaxColumnsPerTable, nameof(MaxColumnsPerTable));
            Check(MaxTableIdLength, TableBlockLimits.FormatMaxTableIdLength, nameof(MaxTableIdLength));
            // A read collects one row past its limit to know where the next page starts.
            Check(MaxReadRows, int.MaxValue - 1, nameof(MaxReadRows));
            Check(MaxConcurrentPuts, 1024, nameof(MaxConcurrentPuts));
            Check(MaxTextReadRows, int.MaxValue, nameof(MaxTextReadRows));
            Check(MaxConcurrentReads, 1024, nameof(MaxConcurrentReads));
            Check(MaxOpenBatchDatabases, 100_000, nameof(MaxOpenBatchDatabases));
            Check(MergeMemoryBudgetMb, 1_048_576, nameof(MergeMemoryBudgetMb));
            Check(FeedProductPeriodMinutes, 1440, nameof(FeedProductPeriodMinutes));
            Check(FeedTablePeriodMinutes, 1440, nameof(FeedTablePeriodMinutes));

            // The table period is drained on a tick of the product period.
            if (FeedTablePeriodMinutes % FeedProductPeriodMinutes != 0)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{nameof(FeedTablePeriodMinutes)} = {FeedTablePeriodMinutes} is invalid: " +
                                                    $"expected a multiple of {nameof(FeedProductPeriodMinutes)} ({FeedProductPeriodMinutes}).");

            if (DeleteMergedBlocksAfterHours < 0 || DeleteMergedBlocksAfterHours > 8760)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{nameof(DeleteMergedBlocksAfterHours)} = {DeleteMergedBlocksAfterHours} is invalid: expected 0..8760 hours.");

            if (MinFreeDiskMb < 0)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{nameof(MinFreeDiskMb)} = {MinFreeDiskMb} is invalid: expected 0 (no check) or more megabytes.");

            return new TableBlockOptions
            {
                Limits = new TableBlockLimits(MaxDecompressedBytes, MaxTables, MaxRows, MaxColumnsPerTable, MaxTableIdLength),
                MaxReadRows = MaxReadRows,
                MinFreeDiskMb = MinFreeDiskMb,
                MaxOpenBatchDatabases = MaxOpenBatchDatabases,
                DeleteMergedBlocksAfterHours = DeleteMergedBlocksAfterHours,
                MergeMemoryBudgetMb = MergeMemoryBudgetMb,
            };
        }

        private static void Check(long value, long max, string name)
        {
            if (value < 1 || value > max)
                throw new InvalidOperationException($"{nameof(IServerConfig.TableSensors)}.{name} = {value} is invalid: expected 1..{max}.");
        }
    }
}
