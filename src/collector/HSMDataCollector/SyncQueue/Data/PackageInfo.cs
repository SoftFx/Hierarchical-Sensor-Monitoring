namespace HSMDataCollector.SyncQueue.Data
{
    internal readonly struct PackageInfo
    {
        internal double AvrTimeInQueue { get; }

        internal int ValuesCount { get; }

        internal PackageInfo(double sumTime, int count)
        {
            if (count > 0)
            {
                AvrTimeInQueue = sumTime / count;
                ValuesCount = count;
            }
            else
            {
                AvrTimeInQueue = 0;
                ValuesCount = 0;
            }
        }
    }
}
