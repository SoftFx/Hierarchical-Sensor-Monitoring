namespace HSMDataCollector.SyncQueue.BaseQueue
{
    internal readonly struct PackageInfo
    {
        internal double AvrTimeInQueue { get; }

        internal int ValuesCount { get; }


        internal PackageInfo(double sumTime, int count)
        {
            AvrTimeInQueue = sumTime / count;
            ValuesCount = count;
        }
    }
}