using System;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverFixture : IDisposable
    {
        public MonitoringDataReceiverFixture()
        {
            if (Directory.Exists(DatabaseAdapterManager.DatabaseFolder))
                DeleteDatabaseDirectory();
        }


        public void Dispose() =>  DeleteDatabaseDirectory();

        private static void DeleteDatabaseDirectory() =>
            Directory.Delete(DatabaseAdapterManager.DatabaseFolder, true);
    }
}
