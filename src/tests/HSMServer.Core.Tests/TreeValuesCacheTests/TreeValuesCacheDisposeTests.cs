using System;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // The cache loads the history of the sensors it read at start in the background. DisposeAsync closes the
    // database, so it stops that load and waits for it first: a read left running on a closed LevelDB is a native
    // use-after-free that kills the process (the server-tests host crash on PR #1955, #1943 follow-up).
    public sealed class TreeValuesCacheDisposeTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>, IDisposable
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HeldLongEnough = TimeSpan.FromMilliseconds(500);

        // Field initializers run before the base constructor, which builds the cache and starts the load.
        private readonly int _constructingThread = Environment.CurrentManagedThreadId;
        private readonly ManualResetEventSlim _insideLoad = new();
        private readonly ManualResetEventSlim _releaseLoad = new();
        private int _held;


        public TreeValuesCacheDisposeTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }

        public void Dispose()
        {
            _releaseLoad.Set();
            _insideLoad.Dispose();
            _releaseLoad.Dispose();
        }


        // Holds the first history read of the background load (any thread but the one building the cache).
        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => new FailingDatabaseCore(inner, _ => false)
        {
            FailGetLatestValue = _ =>
            {
                if (Environment.CurrentManagedThreadId != _constructingThread && Interlocked.Exchange(ref _held, 1) == 0)
                {
                    _insideLoad.Set();
                    _releaseLoad.Wait(Patience);
                }

                return null;
            },
        };


        [Fact]
        public async Task DisposeAsync_WaitsForTheBackgroundHistoryLoad_BeforeItClosesTheDatabase()
        {
            Assert.True(_insideLoad.Wait(Patience), "the background history load did not start");

            var dispose = _valuesCache.DisposeAsync().AsTask();

            Assert.False(dispose.Wait(HeldLongEnough), "DisposeAsync closed the database under the running history load");

            _releaseLoad.Set();

            await dispose.WaitAsync(Patience);
        }
    }
}
