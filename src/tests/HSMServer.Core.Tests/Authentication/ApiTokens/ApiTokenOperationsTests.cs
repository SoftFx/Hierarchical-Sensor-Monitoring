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
    }
}
