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

        public IDataSender DataSender { get; set; }

        internal string ServerUrl => ServerAddress.TrimEnd('/');

        public bool AllowUntrustedServerCertificate { get; set; }

        public TimeSpan ExceptionDeduplicatorWindow { get; set; } = TimeSpan.FromHours(1);

        internal void Validate()
        {
            if (MaxQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), "Max queue size must be greater than zero.");

            if (MaxValuesInPackage <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxValuesInPackage), "Max values in package must be greater than zero.");

            if (PackageCollectPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PackageCollectPeriod), "Package collect period must be greater than zero.");

            if (ExceptionDeduplicatorWindow < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ExceptionDeduplicatorWindow), "Exception deduplicator window cannot be negative.");
        }
    }
}
