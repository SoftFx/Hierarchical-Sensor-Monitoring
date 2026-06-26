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

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public IDataSender DataSender { get; set; }

        internal string ServerUrl => ServerAddress.TrimEnd('/');

        public bool AllowUntrustedServerCertificate { get; set; }

        public bool AllowPlaintextTransport { get; set; }

        public TimeSpan ExceptionDeduplicatorWindow { get; set; } = TimeSpan.FromHours(1);

        public int MaxDeduplicatedMessages { get; set; } = 1000;

        public int MaxSensors { get; set; } = 100000;

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(AccessKey))
                throw new ArgumentNullException(nameof(AccessKey));

            if (string.IsNullOrWhiteSpace(ServerAddress))
                throw new ArgumentNullException(nameof(ServerAddress));

            if (Port <= 0 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), "Port must be in the range 1..65535.");

            if (MaxQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), "Max queue size must be greater than zero.");

            if (MaxValuesInPackage <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxValuesInPackage), "Max values in package must be greater than zero.");

            if (PackageCollectPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PackageCollectPeriod), "Package collect period must be greater than zero.");

            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "Request timeout must be greater than zero.");

            if (ExceptionDeduplicatorWindow < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ExceptionDeduplicatorWindow), "Exception deduplicator window cannot be negative.");

            if (MaxDeduplicatedMessages <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxDeduplicatedMessages), "Max deduplicated messages must be greater than zero.");

            if (MaxSensors <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxSensors), "Max sensors must be greater than zero.");
        }
    }
}
