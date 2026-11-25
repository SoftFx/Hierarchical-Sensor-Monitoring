using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.SealedHistory;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// What a read of one table sensor hands the rows of its range to (#1847): the rows collected for an answer
    /// (<see cref="TableRowCollector"/>) or reduced for a chart (<see cref="TableChartReducer"/>). The read decides which
    /// stored blocks and which batches of merged hours hold the range and visits them in the order of their earliest row;
    /// the sink takes their rows and says when the rest cannot matter.
    /// </summary>
    internal interface ITableRowSink
    {
        /// <summary>Takes the rows of the table <paramref name="tableId"/> out of one stored block (a failure counted, a store fault thrown).</summary>
        void AddBlock(ITableBlockStore store, in TableBlockIndexEntry entry, byte[] tableId);

        /// <summary>Takes the rows of one decoded batch of a merged hour; false (counted) when its columns are not the sensor's.</summary>
        bool AddBatch(TableBatch batch, long hourTicks, int ordinal, bool moved);

        /// <summary>Counts a batch that could not be read.</summary>
        void BatchFailed();

        /// <summary>
        /// Counts blocks of committed hours that the merge could not read and kept (listed in their hour's record, #1850 part
        /// 4b): failed blocks, as they were before their hour was merged.
        /// </summary>
        void CountKeptUnreadableBlocks(int count);

        /// <summary>True when the read can stop before a source whose earliest row in the range is at <paramref name="nextMinTime"/>.</summary>
        bool CanStopBefore(long nextMinTime);
    }
}
