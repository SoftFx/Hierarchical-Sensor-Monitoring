namespace HSMDataCollector.Extensions
{
    internal static class MemorySizeExtensions
    {
        private const int MBDivisor = 1 << 20;


        internal static int ToMegabytes(this double value) => (int)(value / MBDivisor);

        internal static int ToMegabytes(this long value) => (int)(value / MBDivisor);
    }
}
