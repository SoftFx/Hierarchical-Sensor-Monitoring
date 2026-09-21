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
public class PolicyIdsIndexRaceTests : DatabaseCoreTestsBase<PolicyIdsIndexRaceFixture>, IClassFixture<DatabaseRegisterFixture>
{
    private const int ThreadCount = 8;
    private const int IdsPerThread = 25;

    // A stuck lock must fail the suite, not hang the CI agent.
    private static readonly TimeSpan ConcurrencyTimeout = TimeSpan.FromMinutes(1);


    public PolicyIdsIndexRaceTests(PolicyIdsIndexRaceFixture fixture, DatabaseRegisterFixture registerFixture)
        : base(fixture, registerFixture) { }


    // Deterministic POST-FIX (the lock serializes the whole read-append-put):
    // every id from every concurrent writer must be in the index. Pre-fix
    // this intermittently lost ids — exactly the incident's mechanism.
    [Fact]
    public void AddPolicy_ConcurrentWriters_AllIdsIndexed()
    {
        var allIds = Enumerable.Range(0, ThreadCount * IdsPerThread).Select(_ => Guid.NewGuid()).ToList();
        using var start = new Barrier(ThreadCount);

        // LongRunning: dedicated threads, not the thread pool — on a small
        // CI agent pool injection (roughly a thread per 500 ms) delays the
        // barrier release and shrinks the very overlap this test exists to
        // create.
        var threads = Enumerable.Range(0, ThreadCount).Select(t => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();

            foreach (var id in allIds.Skip(t * IdsPerThread).Take(IdsPerThread))
                _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        }, TaskCreationOptions.LongRunning)).ToArray();

        Assert.True(Task.WaitAll(threads, ConcurrencyTimeout), "Concurrent AddPolicy did not finish in time — the index lock looks stuck");

        var indexed = _databaseCoreManager.DatabaseCore.GetAllPolicies()
            .Select(p => new Guid(p.Id))
            .ToHashSet();

        Assert.All(allIds, id => Assert.Contains(id, indexed));
    }


    // The mirror arm: a concurrent remover must not cost the adder its ids —
    // the lost-update window pre-fix ran in BOTH directions (a remover whose
    // read predated the adder's put restored the removed ids to the index
    // while dropping the added ones). The assertion reads through
    // GetAllPolicies, which DROPS ids whose row fetch returns null, so the
    // removedIds check cannot distinguish "gone from the index" from "row
    // gone, index state unknown" — it pins only that a removed id never
    // resurfaces as a live policy. The removed-from-the-index property
    // itself is not observable through IDatabaseCore (the raw index is
    // private to the LevelDB worker) and rests on the same lock the
    // addedIds assertion does regress.
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

        using var barrier = new Barrier(2);

        var adder = Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            foreach (var id in addedIds)
                _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        }, TaskCreationOptions.LongRunning);

        var remover = Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            foreach (var id in removedIds)
                _databaseCoreManager.DatabaseCore.RemovePolicy(id);
        }, TaskCreationOptions.LongRunning);

        Assert.True(Task.WaitAll([adder, remover], ConcurrencyTimeout), "Concurrent add-vs-remove did not finish in time — the index lock looks stuck");

        var indexed = _databaseCoreManager.DatabaseCore.GetAllPolicies()
            .Select(p => new Guid(p.Id))
            .ToHashSet();

        Assert.All(addedIds, id => Assert.Contains(id, indexed));
        Assert.All(removedIds, id => Assert.DoesNotContain(id, indexed));
    }


    // The self-heal seam the boot path uses (#1407): GetPolicy reads the row
    // by id DIRECTLY, never through the index. DatabaseCore's add/remove both
    // go through the index, so this test cannot stage a true index-orphaned
    // row through the public interface — what it pins is the fetch's
    // index-independence (the property the heal's row recovery relies on);
    // the orphan scenario itself is pinned by PolicyIndexHealerTests with a
    // row the index-driven dictionary never had. TryGetPolicy carries the
    // same index-independence plus the absent/unreadable split the heal's
    // diagnosis depends on.
    [Fact]
    public void GetPolicy_ByDirectId_AnswersRegardlessOfIndexState()
    {
        var id = Guid.NewGuid();

        _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));
        _databaseCoreManager.DatabaseCore.RemovePolicy(id); // index entry + row gone

        // The row is gone too (RemovePolicy deletes both) — the direct fetch
        // answers null, not an index-driven skip; after a re-add it answers
        // the row whatever the index holds.
        Assert.Null(_databaseCoreManager.DatabaseCore.GetPolicy(id));
        Assert.False(_databaseCoreManager.DatabaseCore.TryGetPolicy(id, out var absent));
        Assert.Null(absent);

        _databaseCoreManager.DatabaseCore.AddPolicy(BuildEntity(id));

        Assert.NotNull(_databaseCoreManager.DatabaseCore.GetPolicy(id));
        Assert.Equal(id, new Guid(_databaseCoreManager.DatabaseCore.GetPolicy(id).Id));
        Assert.True(_databaseCoreManager.DatabaseCore.TryGetPolicy(id, out var present));
        Assert.Equal(id, new Guid(present.Id));
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


public class PolicyIdsIndexRaceFixture : DatabaseFixture
{
    protected override string DatabaseFolder => nameof(PolicyIdsIndexRaceTests);
}
