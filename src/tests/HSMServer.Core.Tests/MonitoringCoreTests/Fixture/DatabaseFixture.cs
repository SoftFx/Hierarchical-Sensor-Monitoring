using HSMCommon;
using HSMServer.Core.DataLayer;
using System.IO;
using System.Threading;

namespace HSMServer.Core.Tests.MonitoringCoreTests.Fixture
{
    public abstract class DatabaseFixture
    {
        private static int _instances;

        private readonly int _instance = Interlocked.Increment(ref _instances);


        protected abstract string DatabaseFolder { get; }

        // One folder per fixture instance: xUnit builds a class fixture per test class, and several
        // classes share one fixture type. With one folder per type the next class's fixture tried to
        // delete a folder whose databases the previous class still held open, and spent 20 s in
        // FileManager's retries every time (#1943).
        internal string DatabasePath => $"TestDB_{DatabaseFolder}_{_instance}";


        public DatabaseFixture()
        {
            if (Directory.Exists(DatabasePath))
                FileManager.SafeRemoveFolder(DatabasePath);
        }


        internal virtual void InitializeDatabase(IDatabaseCore dbCore) { }
    }
}
