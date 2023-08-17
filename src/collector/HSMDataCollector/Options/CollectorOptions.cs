using System;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        public string ServerAddress { get; set; }

        public string AccessKey { get; set; }

        public int Port { get; set; } = 44330;
        

        public string Module { get; set; }


        public int MaxQueueSize { get; set; } = 20000;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageCollectPeriod { get; set; } = TimeSpan.FromSeconds(15);


        internal string ServerUrl => ServerAddress.TrimEnd('/');
    }
}