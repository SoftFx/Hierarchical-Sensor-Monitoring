namespace HSMTableBlocks
{
    /// <summary>
    /// Counts the identifier comparisons of the uniqueness checks. A test passes one to the writer or the reader to prove
    /// the checks stay O(n log n) by counting work instead of measuring time; production code passes none.
    /// </summary>
    internal sealed class IdComparisonCounter
    {
        public long Count;
    }
}
