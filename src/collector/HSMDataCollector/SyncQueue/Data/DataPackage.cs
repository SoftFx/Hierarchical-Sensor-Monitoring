using System.Collections.Generic;


namespace HSMDataCollector.SyncQueue.Data
{
    internal sealed class DataPackage<T>
    {
        internal IEnumerable<T> Items { get; set; }

        private double _time = 0;
        private int _count = 0;

        internal void AddInfo(double time, int count)
        {
            _time += time;
            _count += count;
        }

        internal PackageInfo GetInfo() => new PackageInfo(_time, _count);
    }
}