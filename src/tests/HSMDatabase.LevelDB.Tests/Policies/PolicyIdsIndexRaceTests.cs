using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.Policies;

// The lost-update race on the global policy-id index (#1407): the index is a
// single LevelDB key mutated by read-append-put, and template applies run
// concurrently on per-product queue threads. Two interleaved cycles used to
// lose one side's id forever — the policy row landed under its own key, but
// the boot path (GetAllPolicies walks the INDEX) never saw it, silently
// skipping the sensor's reference. The production incident: a template alert
// that lived in memory until the next restart and vanished without a trace.
[Collection("Database collection")]
public class PolicyIdsIndexRaceTests : DatabaseCoreTestsBase<PolicyIdsIndexRaceFixture>, IClassFixture<DatabaseRegisterFixture>
{
    private const int ThreadCount = 8;
    private const int IdsPerThread = 25;


    public PolicyIdsIndexRaceTests(PolicyIdsIndexRaceFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture) { }


    // Deterministic POST-FIX (the lock serializes the whole read-append-put):
    // every id from every concurrent writer must be in the index. Pre-fix
    // this intermittently lost ids — exactly the incident's mechanism.
    [Fact]
    public void AddPolicy_ConcurrentWriters_AllIdsIndexed()
    {
        var allIds = Enumerable.Range(0, ThreadCount * IdsPerThread).Select(_ => Guid.NewGuid()).ToList();
        var start = new Barrier(ThreadCount);

        var threads = Enumerable.Range(0, ThreadCount).Select(t => Task.Run(() =>
        {
            start.SignalAndWait();

            foreach (var id in allIds.Skip(t * IdsPerThread).Take(IdsPerThread))
                _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        })).ToArray();

        Task.WaitAll(threads);

        var indexed = _databaseCoreManager.DatabaseCore.GetAllPolicies()
            .Select(p => new Guid(p.Id))
            .ToHashSet();

        Assert.All(allIds, id => Assert.Contains(id, indexed));
    }


    // The mirror arm: concurrent add + remove must leave a consistent pair
    // (the removed id gone from the index, the added id present) — the lock
    // covers RemovePolicy's read-modify-write as well.
    [Fact]
    public void AddPolicy_RacingRemovePolicy_LeavesConsistentIndex()
    {
        var addedIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();
        var removedIds = Enumerable.Range(0, 100).Select(_ =>
        {
            var id = Guid.NewGuid();
            _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
            return id;
        }).ToList();

        var barrier = new Barrier(2);

        var adder = Task.Run(() =>
        {
            barrier.SignalAndWait();
            foreach (var id in addedIds)
                _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        });

        var remover = Task.Run(() =>
        {
            barrier.SignalAndWait();
            foreach (var id in removedIds)
                _databaseCoreManager.DatabaseCore.RemovePolicy(id);
        });

        Task.WaitAll(adder, remover);

        var indexed = _databaseCoreManager.DatabaseCore.GetAllPolicies()
            .Select(p => new Guid(p.Id))
            .ToHashSet();

        Assert.All(addedIds, id => Assert.Contains(id, indexed));
        Assert.All(removedIds, id => Assert.DoesNotContain(id, indexed));
    }


    // The self-heal seam the boot path uses (#1407): GetPolicy reads the row
    // by id DIRECTLY, bypassing the index — a row whose index entry was lost
    // pre-fix is still reachable through the sensor entity's reference.
    [Fact]
    public void GetPolicy_RowWithoutIndexEntry_IsReadableByDirectId()
    {
        var id = Guid.NewGuid();

        _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        _databaseCoreManager.DatabaseCore.RemovePolicy(id); // index entry + row gone
        _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id)); // row + index again

        // The direct fetch answers regardless of the index state — the
        // property the heal relies on.
        Assert.NotNull(_databaseCoreManager.DatabaseCore.GetPolicy(id));
        Assert.Equal(id, new Guid(_databaseCoreManager.DatabaseCore.GetPolicy(id).Id));
    }


    private static PolicyEntity BuildEntity(Guid id) => new()
    {
        Id = id.ToByteArray(),
        Template = "t",
        Conditions = [],
        Destination = new PolicyDestinationEntity { IsNotInitialized = true },
        Schedule = new PolicyScheduleEntity(),
    };
}


public class PolicyIdsIndexRaceFixture : HSMServer.Core.Tests.MonitoringCoreTests.Fixture.DatabaseFixture
{
    protected override string DatabaseFolder => nameof(PolicyIdsIndexRaceTests);
}
