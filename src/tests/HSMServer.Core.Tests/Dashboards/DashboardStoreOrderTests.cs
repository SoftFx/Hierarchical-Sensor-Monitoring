using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Dashboards;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Dashboards
{
    /// <summary>
    /// A dashboard is stored whole, as a snapshot, by whoever changed it: a request
    /// thread for a user's edit and the cache's thread for a source a panel
    /// subscription adds. Two stores of one dashboard must reach the store in the
    /// order their snapshots were taken, or the older one stays there.
    /// </summary>
    public class DashboardStoreOrderTests
    {
        private static readonly TimeSpan _safetyTimeout = TimeSpan.FromSeconds(10);


        // Before the fix the snapshot and its write were two unguarded steps: a
        // store that was slow to write was overtaken by a later one and then wrote
        // its older snapshot over it. The later change was in memory only, and a
        // restart lost it.
        [Fact]
        public async Task StoreThatIsStillWriting_IsNotOvertakenByALaterOne()
        {
            var collection = new HoldingDashboardCollection();

            var database = new Mock<IDatabaseCore>();
            database.Setup(d => d.Dashboards).Returns(collection);

            var dashboards = new DashboardManager(database.Object, new Mock<ITreeValuesCache>().Object);

            try
            {
                Assert.True(await dashboards.TryAdd(new DashboardAdd { Name = "board", AuthorId = Guid.Empty }, out var dashboard));

                // The first store has taken its snapshot (no panels) and is inside the write.
                collection.HoldNextUpdate();

                var first = Task.Run(() => dashboards.TryUpdate(dashboard));
                await collection.Held.WaitAsync(_safetyTimeout);

                // The second one changes the dashboard and stores it with the panel.
                var second = Task.Run(() => dashboard.TryAddPanel(new Panel(dashboard)));

                // The wait only gives an unguarded second store the time to overtake the
                // first one. A guarded one cannot, however long this is, so the test
                // passes on any timing.
                await Task.WhenAny(collection.OvertakenByALaterUpdate, Task.Delay(TimeSpan.FromMilliseconds(500)));

                collection.Release();

                await Task.WhenAll(first, second).WaitAsync(_safetyTimeout);

                Assert.Equal(2, collection.Updates);
                Assert.Single(collection.LastWritten.Panels);
            }
            finally
            {
                collection.Release();
                dashboards.Dispose();
            }
        }


        // Writes what it is given, in the order the writes complete; the next update can be held inside the write.
        private sealed class HoldingDashboardCollection : IDashboardCollection
        {
            private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _overtaken = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly ManualResetEventSlim _gate = new(false);

            private int _holdNext;
            private int _updates;


            public Task Held => _held.Task;

            public Task OvertakenByALaterUpdate => _overtaken.Task;

            public int Updates => Volatile.Read(ref _updates);

            public DashboardEntity LastWritten { get; private set; }


            public void HoldNextUpdate() => Volatile.Write(ref _holdNext, 1);

            public void Release() => _gate.Set();


            public void AddEntity(DashboardEntity entity) => LastWritten = entity;

            public void UpdateEntity(DashboardEntity entity)
            {
                if (Interlocked.Exchange(ref _holdNext, 0) == 1)
                {
                    _held.TrySetResult();
                    _gate.Wait(_safetyTimeout);
                }
                else if (_held.Task.IsCompleted && !_gate.IsSet)
                    _overtaken.TrySetResult();

                lock (_gate)
                {
                    LastWritten = entity;
                    _updates++;
                }
            }

            public void RemoveEntity(Guid id) { }

            public bool TryReadEntity(Guid id, out DashboardEntity entity)
            {
                entity = null;
                return false;
            }

            public List<DashboardEntity> ReadCollection() => [];

            public TaskResult<string> Backup(string backupPath) => throw new NotSupportedException();
        }
    }
}
