namespace HSMDataCollector.Extensions
{
    internal static class MemorySizeExtensions
    {
        private const int ByteToMbDivisor = 1 << 20;
        private const int KbToMbDivisor = 1 << 10;


        internal static int BytesToMegabytes(this double value) => (int)(value / ByteToMbDivisor);

        internal static int BytesToMegabytes(this long value) => (int)(value / ByteToMbDivisor);

        internal static int KilobytesToMegabytes(this long value) => (int)(value / KbToMbDivisor);
    }
}
