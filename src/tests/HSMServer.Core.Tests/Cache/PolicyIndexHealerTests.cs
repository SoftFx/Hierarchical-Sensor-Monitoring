using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using Xunit;

namespace HSMServer.Core.Tests.Cache
{
    // The boot path's self-heal over the policy-id index (#1407): rows whose
    // index entry was lost to the (now locked) concurrent-index race are
    // recovered through the sensor entities' own references. Pinned at the
    // healer unit level — the database-level race and direct-fetch seams live
    // in HSMDatabase.LevelDB.Tests (PolicyIdsIndexRaceTests).
    public class PolicyIndexHealerTests
    {
        [Fact]
        public void ReferencedIdMissingFromIndex_RowExists_IsHealed()
        {
            // The index-driven dictionary has the OTHER policy, not the orphan.
            var indexedRow = BuildRow();
            var orphanRow = BuildRow();
            var sensor = BuildSensor(new Guid(indexedRow.Id).ToString(), new Guid(orphanRow.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity>
            {
                [new Guid(indexedRow.Id).ToString()] = indexedRow,
            };

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, id => id == new Guid(orphanRow.Id) ? orphanRow : null);

            Assert.Equal(1, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.True(policies.ContainsKey(new Guid(orphanRow.Id).ToString()));
            Assert.Same(orphanRow, policies[new Guid(orphanRow.Id).ToString()]);
        }


        [Fact]
        public void ReferencedId_RowMissingEverywhere_IsUnresolved()
        {
            var deadId = Guid.NewGuid();
            var sensor = BuildSensor(deadId.ToString());
            var policies = new Dictionary<string, PolicyEntity>();

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => null);

            Assert.Equal(0, healed);
            Assert.Equal([deadId], unresolved);
            Assert.Empty(unread);
            Assert.Empty(policies);
        }


        [Fact]
        public void ReferencedId_RowUnreadable_IsUnreadNotUnresolved()
        {
            // A read failure is not an orphan verdict: the id lands in Unread,
            // the caller reports "could not read" and the next boot retries.
            var id = Guid.NewGuid();
            var sensor = BuildSensor(id.ToString());
            var policies = new Dictionary<string, PolicyEntity>();

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => throw new InvalidOperationException("disk on fire"));

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Equal([id], unread);
            Assert.Empty(policies);
        }


        [Fact]
        public void ReferencedIdAlreadyInDictionary_NotTouched()
        {
            var row = BuildRow();
            var sensor = BuildSensor(new Guid(row.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity> { [new Guid(row.Id).ToString()] = row };
            var fetched = false;

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => { fetched = true; return row; });

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.False(fetched);
            Assert.Single(policies);
        }


        [Fact]
        public void DuplicateReferences_HealOnce()
        {
            // Two sensors referencing the same orphaned id: one heal, one
            // dictionary entry — and the second pass must not even fetch.
            var orphanRow = BuildRow();
            var sensorA = BuildSensor(new Guid(orphanRow.Id).ToString());
            var sensorB = BuildSensor(new Guid(orphanRow.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity>();
            var fetches = 0;

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensorA, sensorB], policies,
                id =>
                {
                    fetches++;
                    return id == new Guid(orphanRow.Id) ? orphanRow : null;
                });

            Assert.Equal(1, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.Equal(1, fetches);
            Assert.Single(policies);
        }


        [Fact]
        public void DuplicateDeadReferences_ReportedOnceAndFetchedOnce()
        {
            // Many sensors can share one dead id: one unresolved entry, one
            // fetch — the boot Warn counts DISTINCT ids.
            var deadId = Guid.NewGuid();
            var sensorA = BuildSensor(deadId.ToString());
            var sensorB = BuildSensor(deadId.ToString());
            var fetches = 0;

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensorA, sensorB], new Dictionary<string, PolicyEntity>(),
                _ => { fetches++; return null; });

            Assert.Equal(0, healed);
            Assert.Equal([deadId], unresolved);
            Assert.Empty(unread);
            Assert.Equal(1, fetches);
        }


        [Fact]
        public void NonCanonicalReferenceFormat_HealsUnderTheRawKey()
        {
            // The consumer (ApplyPolicies) looks the row up by the RAW
            // reference string, so the heal keys by it too — a normalized
            // key would "heal" into a lookup the consumer still misses (the
            // invisible-loss class this fix exists to end).
            var orphanRow = BuildRow();
            var rawReference = new Guid(orphanRow.Id).ToString("D").ToUpperInvariant();
            var sensor = BuildSensor(rawReference);
            var policies = new Dictionary<string, PolicyEntity>();

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, id => id == new Guid(orphanRow.Id) ? orphanRow : null);

            Assert.Equal(1, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.True(policies.ContainsKey(rawReference));
            Assert.Same(orphanRow, policies[rawReference]);
        }


        [Fact]
        public void NonCanonicalReference_RowIndexedUnderNormalizedKey_GainsRawAliasWithoutHeal()
        {
            // A non-canonical reference whose row IS indexed (under the
            // normalized key the load builds) is not "missing": it gets the
            // raw-key alias the consumer's lookup needs — no fetch, no heal
            // count, so it does not re-"heal" on every boot.
            var row = BuildRow();
            var normalizedKey = new Guid(row.Id).ToString();
            var rawReference = normalizedKey.ToUpperInvariant();
            var sensor = BuildSensor(rawReference);
            var policies = new Dictionary<string, PolicyEntity> { [normalizedKey] = row };
            var fetched = false;

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => { fetched = true; return row; });

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.False(fetched);
            Assert.Same(row, policies[normalizedKey]);
            Assert.Same(row, policies[rawReference]);
            Assert.Equal(2, policies.Count);
        }


        [Fact]
        public void UnparseableReference_IsSkippedSilently()
        {
            // Pre-existing corruption, not this heal's concern: the reference
            // is neither healed nor unresolved — left exactly as the load
            // would have seen it.
            var sensor = BuildSensor("not-a-guid");
            var policies = new Dictionary<string, PolicyEntity>();
            var fetched = false;

            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => { fetched = true; return null; });

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
            Assert.False(fetched);
        }


        [Fact]
        public void NullEntityAndNullReferences_AreTolerated()
        {
            var (healed, unresolved, unread) = PolicyIndexHealer.Heal(null, new Dictionary<string, PolicyEntity>(), _ => null);

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);

            (healed, unresolved, unread) = PolicyIndexHealer.Heal([BuildSensor(null)], new Dictionary<string, PolicyEntity>(), _ => null);

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);

            // A null ELEMENT matches the defensiveness of the container
            // guards; the load's own path filters nulls, so it cannot occur.
            (healed, unresolved, unread) = PolicyIndexHealer.Heal([null, BuildSensor(null)], new Dictionary<string, PolicyEntity>(), _ => null);

            Assert.Equal(0, healed);
            Assert.Empty(unresolved);
            Assert.Empty(unread);
        }


        private static PolicyEntity BuildRow(Guid? id = null) => new()
        {
            Id = (id ?? Guid.NewGuid()).ToByteArray(),
            Template = "t",
            Conditions = [],
            Destination = new PolicyDestinationEntity { IsNotInitialized = true },
            Schedule = new PolicyScheduleEntity(),
        };

        private static SensorEntity BuildSensor(params string[] policyIds) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = Guid.NewGuid().ToString(),
            Policies = policyIds is null ? null : [.. policyIds],
        };
    }
}
