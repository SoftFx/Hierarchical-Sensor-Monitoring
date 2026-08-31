using System;
using System.IO;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;
using HSMDatabase.LevelDB.DatabaseImplementations;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.ApiTokensDBTests
{
    // Worker-level contract of the API token store: persist-first insert with collision
    // detection, atomic rotation batch, prefix-scoped reads, and durable revocation
    // generations. Follows the standalone temp-directory pattern of
    // LevelDBDatabaseAdapterReadOnlyTests.
    public sealed class ApiTokenStoreTests : IDisposable
    {
        private static readonly Guid OwnerId = Guid.NewGuid();

        // 22 canonical Base64URL characters — the real TokenId length — so the keys also
        // pass the manager-level IsValidTokenId if a literal is ever copied over.
        private static readonly string TokenIdA = new('A', 22);
        private static readonly string TokenIdB = new('B', 22);
        private static readonly string TokenIdC = new('C', 22);

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hsm-api-token-tests-{Guid.NewGuid():N}");
        private readonly EnvironmentDatabaseWorker _worker;


        public ApiTokenStoreTests()
        {
            Directory.CreateDirectory(_dbPath);
            _worker = new EnvironmentDatabaseWorker(_dbPath);
        }


        public void Dispose()
        {
            _worker.Dispose();
            TryDeleteDirectory(_dbPath);
        }


        private static void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // LevelDB releases file handles asynchronously on Windows; a leftover temp
                // directory is harmless for the test outcome.
            }
        }


        [Fact]
        public void TryInsertApiToken_PersistsEntityReadableBack()
        {
            var entity = BuildEntity();

            var inserted = _worker.TryInsertApiToken(entity);
            var readBack = _worker.GetApiToken(entity.TokenId);

            Assert.True(inserted);
            Assert.NotNull(readBack);
            Assert.Equal(entity.EntityId, readBack.EntityId);
            Assert.Equal(entity.TokenId, readBack.TokenId);
            Assert.Equal(entity.VersionByte, readBack.VersionByte);
            Assert.Equal(entity.Verifier, readBack.Verifier);
            Assert.Equal(entity.OwnerUserId, readBack.OwnerUserId);
            Assert.Equal(entity.Name, readBack.Name);
            Assert.Equal(entity.Grants.Count, readBack.Grants.Count);
            Assert.Equal(entity.Grants[0].Operation, readBack.Grants[0].Operation);
            Assert.Equal(entity.Grants[0].BoundaryKind, readBack.Grants[0].BoundaryKind);
            Assert.Equal(entity.Grants[0].BoundaryId, readBack.Grants[0].BoundaryId);
            Assert.Null(readBack.RevokedAtUtc);
        }

        [Fact]
        public void TryInsertApiToken_SameTokenIdTwice_ReturnsFalseAndKeepsOriginal()
        {
            var first = BuildEntity(tokenId: TokenIdA);
            var second = BuildEntity(tokenId: TokenIdA, owner: Guid.NewGuid());

            Assert.True(_worker.TryInsertApiToken(first));
            Assert.False(_worker.TryInsertApiToken(second));

            var survivor = _worker.GetApiToken(first.TokenId);

            Assert.Equal(first.EntityId, survivor.EntityId);
            Assert.Equal(first.OwnerUserId, survivor.OwnerUserId);
        }

        [Fact]
        public void GetApiToken_UnknownTokenId_ReturnsNull()
        {
            Assert.Null(_worker.GetApiToken(TokenIdB));
        }

        [Fact]
        public void ReadAllApiTokens_ReturnsOnlyTokenRows()
        {
            _worker.TryInsertApiToken(BuildEntity(tokenId: TokenIdA));
            _worker.TryInsertApiToken(BuildEntity(tokenId: TokenIdC));
            _worker.AdvanceGlobalRevocationGeneration();
            _worker.AdvanceOwnerRevocationGeneration(OwnerId);

            var all = _worker.ReadAllApiTokens();

            // The prefix scan must not pick up generation rows under "ApiTokenGeneration_*".
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public void RemoveApiToken_DeletesRow()
        {
            var entity = BuildEntity(tokenId: TokenIdA);

            Assert.True(_worker.TryInsertApiToken(entity));

            _worker.RemoveApiToken(entity.TokenId);

            Assert.Null(_worker.GetApiToken(entity.TokenId));
            Assert.Empty(_worker.ReadAllApiTokens());
        }

        [Fact]
        public void TryRotateApiToken_WritesRevokedOldAndReplacementAtomically()
        {
            var old = BuildEntity(tokenId: TokenIdA);
            Assert.True(_worker.TryInsertApiToken(old));

            var replacement = BuildEntity(tokenId: TokenIdC, owner: old.OwnerUserId);
            var revokedOld = old with { RevokedAtUtc = DateTime.UtcNow.Ticks, RevocationReason = "rotated" };

            Assert.True(_worker.TryRotateApiToken(revokedOld, replacement));

            var storedOld = _worker.GetApiToken(old.TokenId);
            var storedNew = _worker.GetApiToken(replacement.TokenId);

            Assert.NotNull(storedOld);
            Assert.NotNull(storedNew);
            Assert.NotNull(storedOld.RevokedAtUtc);
            Assert.Equal(replacement.EntityId, storedNew.EntityId);
            Assert.Equal("rotated", storedOld.RevocationReason);
        }

        [Fact]
        public void TryRotateApiToken_ReplacementTokenIdCollision_ReturnsFalseAndKeepsRows()
        {
            var first = BuildEntity(tokenId: TokenIdA);
            var second = BuildEntity(tokenId: TokenIdC);
            Assert.True(_worker.TryInsertApiToken(first));
            Assert.True(_worker.TryInsertApiToken(second));

            var revokedSecond = second with { RevokedAtUtc = DateTime.UtcNow.Ticks };
            var replacement = BuildEntity(tokenId: first.TokenId, owner: second.OwnerUserId);

            Assert.False(_worker.TryRotateApiToken(revokedSecond, replacement));

            // Neither row changed: the source is not revoked and the collision target is intact.
            Assert.Null(_worker.GetApiToken(first.TokenId).RevokedAtUtc);
            Assert.Null(_worker.GetApiToken(second.TokenId).RevokedAtUtc);
        }

        [Fact]
        public void Generations_MissingStateReadsAsZero()
        {
            Assert.Equal(0, _worker.GetGlobalRevocationGeneration());
            Assert.Equal(0, _worker.GetOwnerRevocationGeneration(OwnerId));
        }

        // LevelDB locks its directory exclusively, so durability/corruption tests own a
        // private path and dispose their handle before reopening, instead of reusing the
        // fixture worker's handle.
        [Fact]
        public void Generations_AdvanceIsMonotonicAndDurable()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hsm-api-token-gen-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);

            try
            {
                using (var first = new EnvironmentDatabaseWorker(path))
                {
                    Assert.Equal(1, first.AdvanceGlobalRevocationGeneration());
                    Assert.Equal(2, first.AdvanceGlobalRevocationGeneration());
                    Assert.Equal(1, first.AdvanceOwnerRevocationGeneration(OwnerId));
                    Assert.Equal(3, first.AdvanceGlobalRevocationGeneration());
                }

                using var reopened = new EnvironmentDatabaseWorker(path);

                Assert.Equal(3, reopened.GetGlobalRevocationGeneration());
                Assert.Equal(1, reopened.GetOwnerRevocationGeneration(OwnerId));
                Assert.Equal(0, reopened.GetOwnerRevocationGeneration(Guid.NewGuid()));
            }
            finally
            {
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public void Generations_CorruptGlobalState_ThrowsOnRead()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hsm-api-token-corrupt-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);

            try
            {
                using (var writer = new LevelDBDatabaseAdapter(path))
                {
                    writer.Put(System.Text.Encoding.UTF8.GetBytes("ApiTokenGeneration_Global"),
                        System.Text.Encoding.UTF8.GetBytes("not-a-number"));
                }

                using var worker = new EnvironmentDatabaseWorker(path);

                Assert.Throws<ServerDatabaseException>(() => worker.GetGlobalRevocationGeneration());
            }
            finally
            {
                TryDeleteDirectory(path);
            }
        }


        private static ApiTokenEntity BuildEntity(string tokenId = null, Guid? owner = null) => new()
        {
            EntityVersion = 1,
            EntityId = Guid.NewGuid(),
            TokenId = tokenId ?? TokenIdA,
            VersionByte = 0x01,
            Verifier = new byte[32],
            OwnerUserId = owner ?? OwnerId,
            GlobalRevocationGenerationAtIssue = 0,
            OwnerRevocationGenerationAtIssue = 0,
            Name = $"token-{Guid.NewGuid():N}",
            Description = null,
            Grants =
            [
                new ApiTokenGrantEntity
                {
                    Operation = "alerts:read",
                    BoundaryKind = (byte)ApiTokenBoundaryKind.Product,
                    BoundaryId = Guid.NewGuid().ToString(),
                },
            ],
            CreatedAtUtc = DateTime.UtcNow.Ticks,
            CreatedBy = "test",
        };
    }
}
