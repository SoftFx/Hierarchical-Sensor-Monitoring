using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    [Collection("Database collection")]
    public class AlertTemplatePartialFailureTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;
        private Guid _failProductId = Guid.Empty;


        public AlertTemplatePartialFailureTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner)
        {
            return new FailingDatabaseCore(inner, entity =>
                _failProductId != Guid.Empty &&
                Guid.TryParse(entity.ProductId, out var pid) &&
                pid == _failProductId);
        }


        // Regression for #1127: when one product's UpdateSensor fails during template removal,
        // RemoveAlertTemplateAsync must NOT purge the template from _alertTemplates or _database.
        // Otherwise the failing product's sensors are left with orphaned template policies that
        // can no longer be cleaned up through the normal edit/remove flow.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task RemoveAlertTemplateAsync_OnPartialDbFailure_PreservesTemplate()
        {
            var sensorAPath = "sensorPartialA";
            var sensorBPath = "sensorPartialB";

            var template = BuildIntegerTemplate(TimeSpan.FromMinutes(5));
            template.Paths = [$"*/{sensorAPath}", $"*/{sensorBPath}"];
            Assert.True(template.TryApplyPathTemplates(out _));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var valueA = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorAPath, DateTime.UtcNow);
            var valueB = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorBPath, DateTime.UtcNow);

            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, valueA);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyBId, _fixture.ProductBId, valueB);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorAPath, out var sensorA));
            Assert.Contains(sensorA.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductBId, sensorBPath, out var sensorB));
            Assert.Contains(sensorB.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            _failProductId = _fixture.ProductBId;

            var (success, error) = await _valuesCache.RemoveAlertTemplateAsync(template.Id);

            Assert.False(success);
            Assert.Contains("ProductB_concurrency", error);

            Assert.NotNull(_valuesCache.GetAlertTemplate(template.Id));

            var dbTemplates = _databaseCoreManager.DatabaseCore.GetAllAlertTemplates();
            Assert.Contains(dbTemplates, t => new Guid(t.Id) == template.Id);
        }


        private AlertTemplateModel BuildIntegerTemplate(TimeSpan ttlInterval)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttlInterval.Ticks));

            var model = new AlertTemplateModel
            {
                Name = $"Partial-failure template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = ["placeholder"],
                TtlEntries =
                [
                    new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None),
                ],
            };
            model.TryApplyPathTemplates(out _);
            return model;
        }
    }
}
