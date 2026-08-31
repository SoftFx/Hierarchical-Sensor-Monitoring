using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    public sealed class ApiTokenGrantsTests
    {
        [Fact]
        public void TryCanonicalize_ValidGrants_CanonicalizesIdsAndOrder()
        {
            var productId = Guid.NewGuid();
            var folderId = Guid.NewGuid();

            // Deliberately unsorted input with a non-canonical Guid format ("B" braces).
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = productId.ToString("B") },
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Global },
            };

            var result = ApiTokenGrants.TryCanonicalize(grants, out var canonical);

            Assert.True(result);
            Assert.Equal(2, canonical.Count);

            // Deterministic order: operation, then boundary kind (Global=0 < Product=1).
            Assert.Equal((int)ApiTokenBoundaryKind.Global, canonical[0].BoundaryKind);
            Assert.Null(canonical[0].BoundaryId);
            Assert.Equal(productId.ToString(), canonical[1].BoundaryId);
        }

        [Fact]
        public void TryCanonicalize_SameInputDifferentOrder_ProducesSameCanonicalList()
        {
            var productId = Guid.NewGuid();

            var first = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "sensors:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = productId.ToString() },
                new() { Operation = "products:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Global },
            };

            var second = new List<ApiTokenGrantEntity>(first);
            second.Reverse();

            Assert.True(ApiTokenGrants.TryCanonicalize(first, out var canonicalFirst));
            Assert.True(ApiTokenGrants.TryCanonicalize(second, out var canonicalSecond));

            Assert.Equal(canonicalFirst, canonicalSecond);
        }

        [Fact]
        public void TryCanonicalize_EmptyOrNullList_IsValidEmptyGrantSet()
        {
            Assert.True(ApiTokenGrants.TryCanonicalize([], out var fromEmpty));
            Assert.Empty(fromEmpty);

            Assert.True(ApiTokenGrants.TryCanonicalize(null, out var fromNull));
            Assert.Empty(fromNull);
        }

        [Theory]
        [InlineData("*")]
        [InlineData("admin")]
        [InlineData("alerts")]
        [InlineData("alerts:read ")]
        [InlineData("ALERTS:READ")]
        [InlineData("alerts:delete")]
        [InlineData("users:read")]
        [InlineData("access-keys:read")]
        [InlineData("credentials:read")]
        [InlineData("server-settings:read")]
        [InlineData("")]
        [InlineData(null)]
        public void TryCanonicalize_UnknownOperation_FailsClosed(string operation)
        {
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = operation, BoundaryKind = (byte)ApiTokenBoundaryKind.Global },
            };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Fact]
        public void TryCanonicalize_UnknownBoundaryKind_FailsClosed()
        {
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = 7, BoundaryId = Guid.NewGuid().ToString() },
            };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Fact]
        public void TryCanonicalize_GlobalGrantWithBoundaryId_FailsClosed()
        {
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Global, BoundaryId = Guid.NewGuid().ToString() },
            };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-guid")]
        [InlineData("12345678-1234-1234-1234-12345678901Z")]
        public void TryCanonicalize_ResourceGrantWithInvalidBoundaryId_FailsClosed(string boundaryId)
        {
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = boundaryId },
            };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Fact]
        public void TryCanonicalize_DuplicatePairs_FailClosed()
        {
            var boundaryId = Guid.NewGuid().ToString();

            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = boundaryId },
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = boundaryId },
            };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Fact]
        public void TryCanonicalize_SameOperationDifferentBoundaries_IsAllowed()
        {
            var grants = new List<ApiTokenGrantEntity>
            {
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = Guid.NewGuid().ToString() },
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = Guid.NewGuid().ToString() },
                new() { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Folder, BoundaryId = Guid.NewGuid().ToString() },
            };

            Assert.True(ApiTokenGrants.TryCanonicalize(grants, out var canonical));
            Assert.Equal(3, canonical.Count);
        }

        [Fact]
        public void TryCanonicalize_NullGrantInList_FailsClosed()
        {
            var grants = new List<ApiTokenGrantEntity> { null };

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out _));
        }

        [Fact]
        public void TryCanonicalize_AboveMaxGrants_FailsClosed()
        {
            var grants = new List<ApiTokenGrantEntity>();

            for (var i = 0; i <= ApiTokenGrants.MaxGrants; i++)
                grants.Add(new ApiTokenGrantEntity
                {
                    Operation = "products:read",
                    BoundaryKind = (byte)ApiTokenBoundaryKind.Product,
                    BoundaryId = Guid.NewGuid().ToString(),
                });

            Assert.False(ApiTokenGrants.TryCanonicalize(grants, out var canonical));
            Assert.Null(canonical);

            // Exactly at the bound is still a legitimate token.
            grants.RemoveAt(grants.Count - 1);

            Assert.True(ApiTokenGrants.TryCanonicalize(grants, out canonical));
            Assert.Equal(ApiTokenGrants.MaxGrants, canonical.Count);
        }
    }
}
