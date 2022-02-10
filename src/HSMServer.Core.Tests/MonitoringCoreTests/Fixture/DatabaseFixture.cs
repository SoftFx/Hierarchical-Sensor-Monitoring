using HSMCommon;
using System.IO;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class DatabaseFixture
    {
        protected abstract string DatabaseFolder { get; }

        internal string DatabasePath => $"TestDB_{DatabaseFolder}";

        public DatabaseFixture()
        {
            if (Directory.Exists(DatabasePath))
                FileManager.SafeRemoveFolder(DatabasePath);
        }
    }
}
