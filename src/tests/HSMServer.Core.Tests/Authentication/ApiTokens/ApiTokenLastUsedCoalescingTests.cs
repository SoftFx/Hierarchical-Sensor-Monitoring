using System;
using HSMServer.Authentication;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Last-used coalescing of ApiTokenManager: MarkUsed never writes synchronously; the
    // durable LastUsedAtUtc lands via the background flush (Dispose drains it in tests),
    // monotonically, and never clobbers a revocation recorded after the use.
    public class ApiTokenLastUsedCoalescingTests : DatabaseCoreTestsBase<ApiTokenLastUsedCoalescingTests.Fixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private static readonly Guid OwnerId = Guid.NewGuid();


        public ApiTokenLastUsedCoalescingTests(Fixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        private ApiTokenManager CreateManager() =>
            new(_databaseCoreManager.DatabaseCore, NullLogger<ApiTokenManager>.Instance);

        private (string TokenId, string FullToken) CreateToken(ApiTokenManager manager)
        {
            manager.TryCreateToken(OwnerId, "token", null, grants: null, expiresAtUtc: null,
                createdBy: "test", out _, out var fullToken);

            return (ApiTokenMaterial.TokenIdOf(fullToken), fullToken);
        }


        [Fact]
        public void MarkUsed_FlushesDurablyOnce()
        {
            string tokenId;
            using (var manager = CreateManager())
            {
                manager.Initialize().Wait();
                (tokenId, _) = CreateToken(manager);

                manager.MarkUsed(tokenId);
            } // Dispose: final flush of the pending window.

            using (var reloaded = CreateManager())
            {
                reloaded.Initialize().Wait();

                var token = reloaded.GetToken(tokenId);

                Assert.NotNull(token);
                Assert.NotNull(token.LastUsedAtUtc);
            }
        }

        [Fact]
        public void MarkUsed_UnknownId_IsIgnored()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.MarkUsed(new string('Z', ApiTokenMaterial.TokenIdLength));

            var ex = Record.Exception(() => manager.MarkUsed(null));
            Assert.Null(ex);
        }

        [Fact]
        public void IsTokenLive_FollowsLifecycle()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();
            var (tokenId, _) = CreateToken(manager);

            Assert.True(manager.IsTokenLive(tokenId));
            Assert.False(manager.IsTokenLive(new string('Z', ApiTokenMaterial.TokenIdLength)));
            Assert.False(manager.IsTokenLive(null));

            manager.TryRevokeToken(manager.GetToken(tokenId).EntityId, "test", "revoked", out _);

            Assert.False(manager.IsTokenLive(tokenId));
        }

        [Fact]
        public void Flush_DoesNotClobberARevocationRecordedAfterUse()
        {
            string tokenId;
            using (var manager = CreateManager())
            {
                manager.Initialize().Wait();
                (tokenId, _) = CreateToken(manager);

                manager.MarkUsed(tokenId);

                // The revocation happens AFTER the use but BEFORE the flush lands: the
                // flushed row must still be the revoked one, with the timestamp merged in.
                manager.TryRevokeToken(EntityIdOf(manager, tokenId), "test", "revoked", out _);
            }

            using (var reloaded = CreateManager())
            {
                reloaded.Initialize().Wait();

                var token = reloaded.GetToken(tokenId);

                Assert.NotNull(token);
                Assert.NotNull(token.RevokedAtUtc);
                Assert.NotNull(token.LastUsedAtUtc);
            }
        }


        private static Guid EntityIdOf(ApiTokenManager manager, string tokenId) =>
            manager.GetToken(tokenId).EntityId;


        public class Fixture : DatabaseFixture
        {
            protected override string DatabaseFolder => nameof(ApiTokenLastUsedCoalescingTests);
        }
    }
}
