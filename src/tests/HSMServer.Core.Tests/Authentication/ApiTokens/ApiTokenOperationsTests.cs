using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Catalog contract: All is the exact grantable set, without duplicates, and IsValid
    // accepts exactly its members — the management UI (later PR) renders grant pickers
    // from All, so the two must not drift apart.
    public sealed class ApiTokenOperationsTests
    {
        [Fact]
        public void All_HasNoDuplicates_AndEveryMemberIsValid()
        {
            var all = ApiTokenOperations.All;

            Assert.Equal(all.Count, all.Distinct().Count());
            Assert.All(all, operation => Assert.True(ApiTokenOperations.IsValid(operation)));
        }

        [Fact]
        public void IsValid_RejectsEverythingOutsideTheCatalog()
        {
            Assert.False(ApiTokenOperations.IsValid(null));
            Assert.False(ApiTokenOperations.IsValid(string.Empty));
            Assert.False(ApiTokenOperations.IsValid("alerts:read "));   // trailing space
            Assert.False(ApiTokenOperations.IsValid("ALERTS:READ"));    // case variant
            Assert.False(ApiTokenOperations.IsValid("alerts:delete"));  // plausible but absent
        }

        [Fact]
        public void All_IsASnapshot_MutatingItCannotAlterTheCatalog()
        {
            // The capability catalog is process-global: a cast back to a mutable
            // collection must not let a consumer grant a capability the file forbids.
            var all = ApiTokenOperations.All;

            Assert.DoesNotContain("users:write", all);
            Assert.Throws<NotSupportedException>(() => ((ICollection<string>)all).Add("users:write"));
            Assert.False(ApiTokenOperations.IsValid("users:write"));
        }

        [Fact]
        public void All_MembersFollowReadWriteNaming_IsWriteMatchesTheSuffix()
        {
            // The owner-privilege rule (writes need the Manager role at the boundary) is
            // derived from the "<resource>:read"/"<resource>:write" naming discipline. A
            // member added outside it (e.g. "alerts:acknowledge") would fail OPEN as a
            // read and become executable by a Viewer-role owner — this test fails the
            // addition instead of downgrading the privilege check.
            Assert.All(ApiTokenOperations.All, operation =>
                Assert.True(
                    operation.EndsWith(":read", StringComparison.Ordinal) ||
                    operation.EndsWith(":write", StringComparison.Ordinal),
                    $"'{operation}' must end with :read or :write — IsWrite derives the required owner role from the suffix"));

            Assert.All(ApiTokenOperations.All, operation =>
                Assert.Equal(
                    operation.EndsWith(":write", StringComparison.Ordinal),
                    ApiTokenOperations.IsWrite(operation)));
        }
    }
}
