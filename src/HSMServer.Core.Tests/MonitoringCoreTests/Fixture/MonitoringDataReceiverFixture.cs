using System;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverFixture : IDisposable
    {
        public void Dispose() =>
            Directory.Delete(DatabaseAdapterManager.DatabaseFolder, true);
    }
}
