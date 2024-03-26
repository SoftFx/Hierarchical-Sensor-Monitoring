using System;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        internal const string LocalhostAddress = "localhost";
        internal const int DefaultPort = 44330;


        public string ServerAddress { get; set; } = LocalhostAddress;

        public string AccessKey { get; set; }

        public string ClientName { get; set; }

        public int Port { get; set; } = DefaultPort;


        public string ComputerName { get; set; }

        public string Module { get; set; }


        public int MaxQueueSize { get; set; } = 20000;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageCollectPeriod { get; set; } = TimeSpan.FromSeconds(15);


        internal string ServerUrl => ServerAddress.TrimEnd('/');
    }
}