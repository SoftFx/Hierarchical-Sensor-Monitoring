using System;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        public string AccessKey { get; set; }

        public string ServerAddress { get; set; }

        public int Port { get; set; } = 44330;
        
        public int MaxQueueSize { get; set; } = 20000;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageCollectPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }
}
