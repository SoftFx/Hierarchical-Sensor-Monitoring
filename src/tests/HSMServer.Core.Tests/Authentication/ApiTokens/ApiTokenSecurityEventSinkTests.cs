using System;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Append-only security-event sink on the real LevelDB store: chronological round
    // trip, credential-free payload, sampling of successes, and the documented loss
    // bounds (bounded queue, failed writes drop-and-count, never block the caller).
    public class ApiTokenSecurityEventSinkTests : DatabaseCoreTestsBase<ApiTokenSecurityEventSinkTests.Fixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly string TokenId = new('S', ApiTokenMaterial.TokenIdLength);


        public ApiTokenSecurityEventSinkTests(Fixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        private ApiTokenSecurityEventSink CreateSink() =>
            new(_databaseCoreManager.DatabaseCore, NullLogger<ApiTokenSecurityEventSink>.Instance);


        [Fact]
        public void RecordedFailuresAndDenials_PersistAndRoundTrip()
        {
            using (var sink = CreateSink())
            {
                sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthFailed, TokenId, OwnerId));
                sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthorizationDenied, TokenId, OwnerId,
                    Operation: ApiTokenOperations.AlertsWrite, TargetId: $"{ApiTokenResourceKind.Product}:{Guid.NewGuid()}"));
            } // Dispose drains the queue before assertions.

            var stored = _databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents();

            Assert.Contains(stored, e => e.Kind == (byte)ApiTokenSecurityEventKind.AuthFailed && e.TokenId == TokenId);
            var denial = Assert.Single(stored, e => e.Kind == (byte)ApiTokenSecurityEventKind.AuthorizationDenied);
            Assert.Equal(ApiTokenOperations.AlertsWrite, denial.Operation);
            Assert.Equal(OwnerId, denial.OwnerUserId);
        }

        [Fact]
        public void Successes_AreSampled_FailuresAlwaysRecorded()
        {
            using (var sink = CreateSink())
            {
                for (var i = 0; i < 16; i++)
                    sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthSucceeded, TokenId, OwnerId));

                sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthFailed, TokenId, OwnerId));
            }

            var stored = _databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents()
                .Where(e => e.TokenId == TokenId)
                .ToList();

            // Exactly one of the 16 sampled successes plus the failure: volume control
            // without losing the security-relevant events.
            Assert.Single(stored, e => e.Kind == (byte)ApiTokenSecurityEventKind.AuthSucceeded);
            Assert.Single(stored, e => e.Kind == (byte)ApiTokenSecurityEventKind.AuthFailed);
        }

        [Fact]
        public void Events_AreChronologicalAndCollisionFree()
        {
            using var sink = CreateSink();

            for (var i = 0; i < 5; i++)
                sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthFailed, TokenId + i, null));

            sink.Dispose();

            var stored = _databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents()
                .Where(e => e.TokenId is not null && e.TokenId.StartsWith(TokenId))
                .ToList();

            Assert.Equal(5, stored.Count);
            Assert.Equal(5, stored.Select(e => e.EventId).Distinct().Count());

            var timestamps = stored.Select(e => e.TimestampUtc).ToList();
            Assert.Equal(timestamps, timestamps.OrderBy(t => t).ToList());
        }

        [Fact]
        public void FailedWrite_DropsAndCounts_NeverThrows()
        {
            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == nameof(FailingDatabaseCore.PutApiTokenSecurityEvent),
            };

            using (var sink = new ApiTokenSecurityEventSink(failing, NullLogger<ApiTokenSecurityEventSink>.Instance))
            {
                sink.Record(new ApiTokenSecurityEvent(ApiTokenSecurityEventKind.AuthFailed, TokenId, OwnerId));
            }

            // The durable row is absent and the drop is counted — a security event can
            // never block or fail the request path that produced it.
            using var drained = CreateSink();
            drained.Dispose();

            Assert.DoesNotContain(_databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents(),
                e => e.Kind == (byte)ApiTokenSecurityEventKind.AuthFailed && e.TokenId == TokenId && e.OwnerUserId == OwnerId);
        }


        public class Fixture : DatabaseFixture
        {
            protected override string DatabaseFolder => nameof(ApiTokenSecurityEventSinkTests);
        }
    }
}
