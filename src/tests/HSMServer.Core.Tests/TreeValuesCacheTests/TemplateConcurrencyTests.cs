using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    [Collection("Database collection")]
    public class TemplateConcurrencyTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;

        public TemplateConcurrencyTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        // Regression for #1125: applying a folder-level alert template while new sensor values
        // are concurrently written to sensors in different products must not corrupt sensor policy
        // lists. Each (template, alert) pair must appear at most once on every sensor.
        [Fact]
        [Trait("Category", "Template application")]
#pragma warning disable xUnit1031
        public async Task ConcurrentTemplateApplyAndSensorUpdates_KeepsPoliciesConsistent()
        {
            const int iterations = 50;

            var sensorAPath = "sensorA";
            var sensorBPath = "sensorB";

            var template = BuildIntegerTemplate(ttlInterval: TimeSpan.FromMinutes(5));
            template.Paths = ["*/sensorA", "*/sensorB"];
            Assert.True(template.TryApplyPathTemplates(out _));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var seedA = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorAPath, DateTime.UtcNow);
            var seedB = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorBPath, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seedA);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyBId, _fixture.ProductBId, seedB);
            await Task.Delay(200);

            for (int iter = 0; iter < iterations; iter++)
            {
                var applyTask = _valuesCache.AddAlertTemplateAsync(template);
                var sendA = SendValue(_fixture.AccessKeyAId, _fixture.ProductAId, sensorAPath);
                var sendB = SendValue(_fixture.AccessKeyBId, _fixture.ProductBId, sensorBPath);

                await Task.WhenAll(applyTask, sendA, sendB);

                Assert.True(applyTask.Result.Success, $"Reapply failed: {applyTask.Result.Error}");
                Assert.True(sendA.Result.IsOk, sendA.Result.Error);
                Assert.True(sendB.Result.IsOk, sendB.Result.Error);
            }

            await Task.Delay(300);

            AssertSensorPoliciesConsistent(_fixture.ProductAId, sensorAPath, template.Id);
            AssertSensorPoliciesConsistent(_fixture.ProductBId, sensorBPath, template.Id);
        }
#pragma warning restore xUnit1031

        // Regression for #1125: RemoveChatsFromPoliciesAsync must walk every product in the folder
        // and dispatch chat removal to each sensor's own queue thread — even when the chat removal
        // flow is invoked from outside any product queue (e.g. from TelegramChatsManager).
        [Fact]
        [Trait("Category", "Template application")]
        public async Task RemoveChatsFromPoliciesAsync_RemovesFromSensorsAcrossProducts()
        {
            var chatToRemove = Guid.NewGuid();
            var chatToKeep = Guid.NewGuid();
            var initiator = InitiatorInfo.AsUser("test_user");

            var sensorAPath = "sensorChatsA";
            var sensorBPath = "sensorChatsB";

            var template = BuildIntegerTemplate(ttlInterval: TimeSpan.FromMinutes(5));
            template.Paths = ["*/sensorChatsA", "*/sensorChatsB"];
            Assert.True(template.TryApplyPathTemplates(out _));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var valueA = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorAPath, DateTime.UtcNow);
            var valueB = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorBPath, DateTime.UtcNow);

            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, valueA);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyBId, _fixture.ProductBId, valueB);
            await Task.Delay(200);

            await AddChatsToSensorTtlPolicy(_fixture.ProductAId, sensorAPath, template.Id, chatToRemove, chatToKeep);
            await AddChatsToSensorTtlPolicy(_fixture.ProductBId, sensorBPath, template.Id, chatToRemove, chatToKeep);

            await _valuesCache.RemoveChatsFromPoliciesAsync(_fixture.FolderId, [chatToRemove], initiator);
            await Task.Delay(300);

            AssertChatRemoved(_fixture.ProductAId, sensorAPath, chatToRemove);
            AssertChatRemoved(_fixture.ProductBId, sensorBPath, chatToRemove);
            AssertChatStillPresent(_fixture.ProductAId, sensorAPath, chatToKeep);
            AssertChatStillPresent(_fixture.ProductBId, sensorBPath, chatToKeep);
        }

        // Regression for #1125: RemoveAlertTemplateAsync must walk sub-products of the folder's
        // root products and dispatch template removal to each affected sensor's own queue thread.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task RemoveAlertTemplateAsync_RemovesFromSensorsInSubProducts()
        {
            var sensorSubPath = "SubProductA_concurrency/sensorSub";

            var template = BuildIntegerTemplate(ttlInterval: TimeSpan.FromMinutes(5));
            template.Paths = ["ProductA_concurrency/SubProductA_concurrency/*"];
            Assert.True(template.TryApplyPathTemplates(out _));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorSubPath, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorSubPath, out var sensorBefore));
            Assert.Contains(sensorBefore.Policies.TTLPolicies, p => p.TemplateId == template.Id);

            await _valuesCache.RemoveAlertTemplateAsync(template.Id);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorSubPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies.TTLPolicies, p => p.TemplateId == template.Id);
        }


        private async Task<TaskResult> SendValue(Guid keyId, Guid productId, string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            return await _valuesCache.AddSensorValueAsync(keyId, productId, value);
        }

        private AlertTemplateModel BuildIntegerTemplate(TimeSpan ttlInterval)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttlInterval.Ticks));

            var model = new AlertTemplateModel
            {
                Name = $"Concurrency template {Guid.NewGuid():N}",
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

        private async Task AddChatsToSensorTtlPolicy(Guid productId, string sensorPath, Guid templateId, params Guid[] chats)
        {
            var found = _valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensor);
            Assert.True(found, $"Sensor {sensorPath} not found");

            var ttl = sensor.Policies.TTLPolicies.First(p => p.TemplateId == templateId);

            // Template-protected TTL policies only allow IsDisabled changes from non-force initiators,
            // so we use AsSystemForce to apply the chat destination update.
            var force = InitiatorInfo.AsSystemForce("test_add_chats");

            var destUpdate = new PolicyDestinationUpdate(PolicyDestinationMode.Custom);
            foreach (var chatId in chats)
                destUpdate.Chats[chatId] = $"Chat {chatId}";

            var ttlUpdate = new PolicyUpdate(ttl, force)
            {
                Destination = destUpdate,
            };

            var sensorUpdate = new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [ttlUpdate],
                Initiator = force,
            };

            var result = await _valuesCache.UpdateSensorAsync(sensorUpdate);
            Assert.True(result.IsOk, result.Error);
        }

        private void AssertSensorPoliciesConsistent(Guid productId, string sensorPath, Guid templateId)
        {
            var found = _valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensor);
            Assert.True(found, $"Sensor {sensorPath} not found in product {productId}");

            var templateTtls = sensor.Policies.TTLPolicies.Where(p => p.TemplateId == templateId).ToList();
            Assert.True(templateTtls.Count <= 1,
                $"Sensor {sensorPath} accumulated {templateTtls.Count} TTL policies for template {templateId} — queue boundary corruption (#1125).");

            var templateAlertIds = sensor.Policies
                .Where(p => p.TemplateId == templateId && p.TemplateAlertId.HasValue)
                .Select(p => p.TemplateAlertId!.Value)
                .ToList();
            Assert.Equal(templateAlertIds.Count, templateAlertIds.Distinct().Count());
        }

        private void AssertChatRemoved(Guid productId, string sensorPath, Guid chatId)
        {
            var found = _valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensor);
            Assert.True(found, $"Sensor {sensorPath} not found in product {productId}");

            foreach (var policy in sensor.Policies)
                Assert.DoesNotContain(chatId, policy.Destination.Chats.Keys);

            foreach (var ttl in sensor.Policies.TTLPolicies)
                Assert.DoesNotContain(chatId, ttl.Destination.Chats.Keys);
        }

        private void AssertChatStillPresent(Guid productId, string sensorPath, Guid chatId)
        {
            var found = _valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensor);
            Assert.True(found, $"Sensor {sensorPath} not found in product {productId}");

            var presentInTtl = sensor.Policies.TTLPolicies.Any(p => p.Destination.Chats.ContainsKey(chatId));
            Assert.True(presentInTtl, $"Chat {chatId} should still be present on sensor {sensorPath}");
        }
    }
}
