using System;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        public string ServerUrl => ServerAddress.TrimEnd('/');


        public string ServerAddress { private get; set; }

        public string AccessKey { get; set; }

        public int Port { get; set; } = 44330;
        
        public int MaxQueueSize { get; set; } = 20000;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageCollectPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }
}
