namespace HSMDataCollector.Extensions
{
    internal static class MemorySizeExtensions
    {
        private const int MbDivisor = 1 << 20;


        internal static int ToMegabytes(this double value) => (int)(value / MbDivisor);

        internal static int ToMegabytes(this long value) => (int)(value / MbDivisor);
    }
}
