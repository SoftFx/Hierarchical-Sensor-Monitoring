using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class ProductOwnedPolicyCleanupTests : MonitoringCoreTestsBase<ProductOwnedPolicyCleanupFixture>
    {
        private readonly ProductOwnedPolicyCleanupFixture _fixture;


        public ProductOwnedPolicyCleanupTests(ProductOwnedPolicyCleanupFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        [Trait("Category", "Node-level alert removal migration")]
        public void Cleanup_RemovesUserAddedPolicy_FromProductEntityPoliciesList()
        {
            var survivingIds = GetPolicyIds();

            Assert.DoesNotContain(_fixture.UserAddedPolicyId, survivingIds);
        }

        [Fact]
        [Trait("Category", "Node-level alert removal migration")]
        public void Cleanup_PreservesTemplateDerivedPolicy_ReferencedByProduct()
        {
            var survivingIds = GetPolicyIds();

            Assert.Contains(_fixture.TemplateDerivedPolicyId, survivingIds);
        }

        [Fact]
        [Trait("Category", "Node-level alert removal migration")]
        public void Cleanup_PreservesTtlPolicies_OnProduct()
        {
            var product = _databaseCoreManager.DatabaseCore.GetProduct(_fixture.ProductWithTtlOnlyId.ToString());

            Assert.NotNull(product);
            Assert.NotEmpty(product.TTLPolicies);
        }

        [Fact]
        [Trait("Category", "Node-level alert removal migration")]
        public void Cleanup_PreservesSensorPolicies()
        {
            var survivingIds = GetPolicyIds();

            Assert.Contains(_fixture.SensorPolicyId, survivingIds);
        }

        [Fact]
        [Trait("Category", "Node-level alert removal migration")]
        public void Cleanup_UpdatesProductEntityPoliciesList_WithSurvivingIds()
        {
            var product = _databaseCoreManager.DatabaseCore.GetProduct(_fixture.ProductWithPoliciesId.ToString());

            Assert.NotNull(product);
            Assert.DoesNotContain(_fixture.UserAddedPolicyId.ToString(), product.Policies);
            Assert.Contains(_fixture.TemplateDerivedPolicyId.ToString(), product.Policies);
        }


        private System.Collections.Generic.HashSet<Guid> GetPolicyIds() =>
            _databaseCoreManager.DatabaseCore.GetAllPolicies()
                .Select(p => new Guid(p.Id))
                .ToHashSet();
    }
}
