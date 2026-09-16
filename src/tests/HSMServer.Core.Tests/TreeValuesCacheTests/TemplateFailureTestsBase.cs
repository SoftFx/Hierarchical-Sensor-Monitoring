using System;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // Shared fail-injection harness for the template partial-failure suites
    // (#1396 review: the per-suite copies had begun to drift). Both suites
    // run on the same TemplateConcurrencyFixture physical database.
    public abstract class TemplateFailureTestsBase : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        protected Guid _failProductId = Guid.Empty;


        protected TemplateFailureTestsBase(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
        }


        // entity.ProductId is the sensor's DIRECT parent product, not its
        // root: the predicate only fails writes of sensors whose parent
        // matches, so a sensor nested deeper (e.g. under SubProductA_concurrency)
        // would NOT get the failure injected and a test targeting it would
        // silently stop testing anything — target the sensor's direct parent.
        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) =>
            new FailingDatabaseCore(inner, entity =>
                _failProductId != Guid.Empty &&
                Guid.TryParse(entity.ProductId, out var pid) &&
                pid == _failProductId);
    }
}
