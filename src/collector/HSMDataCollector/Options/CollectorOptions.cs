using System;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        public string AccessKey { get; set; }

        public string ServerAddress { get; set; }

        public int Port { get; set; } = 44330;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageSendingPeriod { get; set; } = TimeSpan.FromSeconds(15);


        internal string ConnectionAddress => $"{ServerAddress}:{Port}/api/sensors";

        internal string ListEndpoint => $"{ConnectionAddress}/list";

        internal string FileEndpoint => $"{ConnectionAddress}/file";
    }
}
