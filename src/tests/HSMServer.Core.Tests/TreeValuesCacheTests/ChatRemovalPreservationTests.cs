using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
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
    // #1451: removing a chat from a folder re-asserts the FULL policy lists
    // of every affected product and sensor (full-list semantics — a partial
    // list would drop the rest). The updates used to be lossy: TTL intervals
    // were reset to FromParent (a null TTL in full-list semantics is an
    // explicit reset, persisted as TTL = null), ScheduleIds were dropped
    // (scheduled alerts silently became 24/7 — the defect class #1409 fixed
    // for schedule deletion), and template-owned policies lost
    // TemplateId/TemplateAlertId (unmanaged orphans the next apply
    // duplicates) — for EVERY policy of the entity, not just the one losing
    // the chat. Chat removal must be content-preserving: only
    // Destination.Chats may change, in memory and in the persisted entities.
    [Collection("Database collection")]
    public class ChatRemovalPreservationTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public ChatRemovalPreservationTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        // The failure scenario from the issue, end to end: a sensor carries
        // (a) a regular alert with a schedule binding whose Custom destination
        // holds the removed chat, (b) a TTL alert with an explicit interval
        // and a schedule binding holding the chat, (c) a template-owned TTL
        // alert holding the chat, (d) policies that never reference the chat,
        // (e) a template-owned REGULAR alert holding the chat — the regular
        // arm's template guards in SensorPolicyCollection are a distinct
        // path from the TTL arm's; the product carries a TTL alert with an
        // explicit interval holding the chat, and a SUB-product carries one
        // with an interval and a schedule binding (sub-product updates route
        // through the ROOT product's queue). Removing the chat from the
        // folder must touch ONLY the destinations.
        [Fact]
        [Trait("Category", "Chat removal")]
        public async Task RemoveChatsFromPoliciesAsync_PreservesTtlIntervalsScheduleAndTemplateBindings()
        {
            var chatToRemove = Guid.NewGuid();
            var chatToKeep = Guid.NewGuid();
            var initiator = InitiatorInfo.AsUser("test_user");

            var regularCarrierScheduleId = Guid.NewGuid();
            var regularScheduleId = Guid.NewGuid();
            var ttlScheduleId = Guid.NewGuid();
            var subTtlScheduleId = Guid.NewGuid();

            var sensorPath = "sensorChatRemovalPreservation";

            // The template-owned TTL alert: applies on sensor creation and
            // carries TemplateId + TemplateAlertId.
            var template = BuildIntegerTemplate(TimeSpan.FromMinutes(10), sensorPath);
            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            await CreateSensor(sensorPath);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, sensorPath, out var sensor));

            var templateTtl = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(template.Id, templateTtl.TemplateId);
            Assert.NotNull(templateTtl.TemplateAlertId);

            // The template-owned regular alert: minted through the regular
            // arm of template application, with the same ownership stamps.
            var templateRegular = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.NotNull(templateRegular.TemplateAlertId);

            // Seed the sensor's policies: full lists in one update (the
            // full-list semantics drop anything not re-asserted). The
            // template-owned TTL only gains a Custom destination with the
            // chats — AsSystemForce, as in TemplateConcurrencyTests.
            var force = InitiatorInfo.AsSystemForce("test_seed_chat_removal");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = force,
                Policies =
                [
                    RegularUpdate(force, regularCarrierScheduleId, ChatsDestination(chatToRemove, chatToKeep)),
                    RegularUpdate(force, regularScheduleId),
                    // The template-owned regular must be re-asserted here too —
                    // full-list semantics with a FORCE initiator drop any policy
                    // absent from the list, template-owned or not.
                    new PolicyUpdate(templateRegular, force)
                    {
                        Destination = ChatsDestination(chatToRemove, chatToKeep),
                    },
                ],
                TTLPolicies =
                [
                    // TTL is re-asserted explicitly: a null TTL in full-list
                    // semantics is an explicit FromParent reset, and the
                    // policy must enter the removal with its explicit
                    // template-minted interval.
                    new PolicyUpdate(templateTtl, force)
                    {
                        Destination = ChatsDestination(chatToRemove, chatToKeep),
                        TTL = templateTtl.TTLTicks,
                    },
                    TtlUpdate(force, TimeSpan.FromMinutes(30), ttlScheduleId, ChatsDestination(chatToRemove, chatToKeep)),
                    TtlUpdate(force, TimeSpan.FromMinutes(45)),
                ],
            });
            Assert.True(seed.IsOk, seed.Error);

            // The product arm: a TTL policy with an explicit interval whose
            // Custom destination holds the chat.
            var productSeed = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = _fixture.ProductAId,
                Initiator = force,
                TTLPolicies = [TtlUpdate(force, TimeSpan.FromMinutes(15), destination: ChatsDestination(chatToRemove, chatToKeep))],
            }, default);
            Assert.True(productSeed.IsOk, productSeed.Error);

            // The sub-product arm: same shape, but with a schedule binding —
            // the removal routes sub-product TTL updates through the ROOT
            // product's queue (the product arm's dispatch contract).
            var subProductSeed = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = _fixture.SubProductAId,
                Initiator = force,
                TTLPolicies = [TtlUpdate(force, TimeSpan.FromMinutes(20), subTtlScheduleId, ChatsDestination(chatToRemove, chatToKeep))],
            }, default);
            Assert.True(subProductSeed.IsOk, subProductSeed.Error);

            // Capture the live policies before the removal (updates apply
            // in place, so the references stay valid — re-locate by id after).
            // templateRegular (captured above) also holds the chat now, so
            // the manual carrier is singled out by its lack of a template.
            var regularCarrier = Assert.Single(sensor.Policies, p => p.Destination.Chats.ContainsKey(chatToRemove) && p.TemplateId == null);
            var regularScheduled = Assert.Single(sensor.Policies, p => p.ScheduleId == regularScheduleId);
            var ttlCarrier = Assert.Single(sensor.Policies.TTLPolicies, t => t.ScheduleId == ttlScheduleId);
            var ttlUnaffected = Assert.Single(sensor.Policies.TTLPolicies, t => t.TTLInterval.Ticks == TimeSpan.FromMinutes(45).Ticks);

            // Sanity: the policies enter the removal with the explicit state
            // the survival asserts below reason about.
            Assert.Equal(TimeSpan.FromMinutes(10).Ticks, templateTtl.TTLTicks);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, ttlCarrier.TTLTicks);

            Assert.True(_valuesCache.TryGetProduct(_fixture.ProductAId, out var productBefore));
            var productTtlId = Assert.Single(productBefore.Policies.TTLPolicies, t => t.Destination.Chats.ContainsKey(chatToRemove)).Id;

            Assert.True(_valuesCache.TryGetProduct(_fixture.SubProductAId, out var subProductBefore));
            var subTtlId = Assert.Single(subProductBefore.Policies.TTLPolicies).Id;

            await _valuesCache.RemoveChatsFromPoliciesAsync(_fixture.FolderId, [chatToRemove], initiator);

            // In-memory, the policies that CARRIED the chat: it is gone from
            // the destination, and everything else rides through.
            Assert.DoesNotContain(chatToRemove, regularCarrier.Destination.Chats.Keys);
            Assert.Contains(chatToKeep, regularCarrier.Destination.Chats.Keys);
            Assert.Equal(regularCarrierScheduleId, regularCarrier.ScheduleId);

            Assert.DoesNotContain(chatToRemove, ttlCarrier.Destination.Chats.Keys);
            Assert.Contains(chatToKeep, ttlCarrier.Destination.Chats.Keys);
            Assert.Equal(ttlScheduleId, ttlCarrier.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, ttlCarrier.TTLTicks);
            Assert.False(ttlCarrier.IsTTLFromParent);

            Assert.DoesNotContain(chatToRemove, templateTtl.Destination.Chats.Keys);
            Assert.Equal(template.Id, templateTtl.TemplateId);
            Assert.NotNull(templateTtl.TemplateAlertId);
            Assert.Equal(TimeSpan.FromMinutes(10).Ticks, templateTtl.TTLTicks);

            // The template-owned REGULAR policy rides through the regular
            // arm's own template guards (force initiator) with its
            // ownership intact.
            Assert.DoesNotContain(chatToRemove, templateRegular.Destination.Chats.Keys);
            Assert.Contains(chatToKeep, templateRegular.Destination.Chats.Keys);
            Assert.Equal(template.Id, templateRegular.TemplateId);
            Assert.NotNull(templateRegular.TemplateAlertId);

            // In-memory, the policies that NEVER referenced the chat are
            // untouched by the full-list re-assert.
            Assert.Equal(regularScheduleId, regularScheduled.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks, ttlUnaffected.TTLTicks);
            Assert.False(ttlUnaffected.IsTTLFromParent);

            Assert.True(_valuesCache.TryGetProduct(_fixture.ProductAId, out var productAfter));
            var productTtl = Assert.Single(productAfter.Policies.TTLPolicies, t => t.Id == productTtlId);
            Assert.DoesNotContain(chatToRemove, productTtl.Destination.Chats.Keys);
            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, productTtl.TTLTicks);
            Assert.False(productTtl.IsTTLFromParent);

            // The sub-product TTL (dispatched through the root product's
            // queue) keeps its interval and schedule binding.
            Assert.True(_valuesCache.TryGetProduct(_fixture.SubProductAId, out var subProductAfter));
            var subTtl = Assert.Single(subProductAfter.Policies.TTLPolicies, t => t.Id == subTtlId);
            Assert.DoesNotContain(chatToRemove, subTtl.Destination.Chats.Keys);
            Assert.Contains(chatToKeep, subTtl.Destination.Chats.Keys);
            Assert.Equal(subTtlScheduleId, subTtl.ScheduleId);
            Assert.Equal(TimeSpan.FromMinutes(20).Ticks, subTtl.TTLTicks);
            Assert.False(subTtl.IsTTLFromParent);

            // Persisted: a restart must reload the same state — intervals,
            // schedule bindings and template ownership survive the writes
            // the removal performs.
            var storedSensor = _databaseCoreManager.DatabaseCore.GetAllSensors()
                .First(e => e.Id == sensor.Id.ToString());

            var storedTtlCarrier = storedSensor.TTLPolicies.First(p => new Guid(p.Id) == ttlCarrier.Id);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, storedTtlCarrier.TTL);
            Assert.Equal(ttlScheduleId.ToByteArray(), storedTtlCarrier.ScheduleId);
            Assert.DoesNotContain(chatToRemove.ToString(), storedTtlCarrier.Destination.Chats.Keys);

            var storedTtlUnaffected = storedSensor.TTLPolicies.First(p => new Guid(p.Id) == ttlUnaffected.Id);
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks, storedTtlUnaffected.TTL);

            var storedTemplateTtl = storedSensor.TTLPolicies.First(p => new Guid(p.Id) == templateTtl.Id);
            Assert.Equal(template.Id.ToByteArray(), storedTemplateTtl.TemplateId);
            Assert.NotEmpty(storedTemplateTtl.TemplateAlertId);
            Assert.Equal(TimeSpan.FromMinutes(10).Ticks, storedTemplateTtl.TTL);

            var storedTemplateRegular = _databaseCoreManager.DatabaseCore.GetAllPolicies()
                .First(p => new Guid(p.Id) == templateRegular.Id);
            Assert.Equal(template.Id.ToByteArray(), storedTemplateRegular.TemplateId);
            Assert.NotEmpty(storedTemplateRegular.TemplateAlertId);
            Assert.DoesNotContain(chatToRemove.ToString(), storedTemplateRegular.Destination.Chats.Keys);

            var storedRegularCarrier = _databaseCoreManager.DatabaseCore.GetAllPolicies()
                .First(p => new Guid(p.Id) == regularCarrier.Id);
            Assert.Equal(regularCarrierScheduleId.ToByteArray(), storedRegularCarrier.ScheduleId);
            Assert.DoesNotContain(chatToRemove.ToString(), storedRegularCarrier.Destination.Chats.Keys);

            var storedRegularScheduled = _databaseCoreManager.DatabaseCore.GetAllPolicies()
                .First(p => new Guid(p.Id) == regularScheduled.Id);
            Assert.Equal(regularScheduleId.ToByteArray(), storedRegularScheduled.ScheduleId);

            var storedProduct = _databaseCoreManager.DatabaseCore.GetProduct(_fixture.ProductAId.ToString());
            var storedProductTtl = storedProduct.TTLPolicies.First(p => new Guid(p.Id) == productTtlId);
            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, storedProductTtl.TTL);
            Assert.DoesNotContain(chatToRemove.ToString(), storedProductTtl.Destination.Chats.Keys);

            var storedSubProduct = _databaseCoreManager.DatabaseCore.GetProduct(_fixture.SubProductAId.ToString());
            var storedSubTtl = Assert.Single(storedSubProduct.TTLPolicies, p => new Guid(p.Id) == subTtlId);
            Assert.Equal(TimeSpan.FromMinutes(20).Ticks, storedSubTtl.TTL);
            Assert.Equal(subTtlScheduleId.ToByteArray(), storedSubTtl.ScheduleId);
            Assert.DoesNotContain(chatToRemove.ToString(), storedSubTtl.Destination.Chats.Keys);
        }


        // A template with BOTH a TTL entry and a regular alert (distinct
        // condition target, so the minted regular is unambiguous): sensor
        // creation mints one policy per entry, each stamped with the
        // template's Id as TemplateId and the entry's Id as TemplateAlertId.
        private AlertTemplateModel BuildIntegerTemplate(TimeSpan ttlInterval, string sensorPath)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttlInterval.Ticks));

            var regularTemplatePolicy = Policy.BuildPolicy((byte)SensorType.Integer);
            regularTemplatePolicy.UpdatePolicy(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Conditions =
                [
                    new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value, new TargetValue(TargetType.Const, "7")),
                ],
            });

            var model = new AlertTemplateModel
            {
                Name = $"Chat removal template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = [$"*/{sensorPath}"],
                TtlEntries =
                [
                    new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None),
                ],
                Policies = [regularTemplatePolicy],
            };
            model.TryApplyPathTemplates(out _);
            return model;
        }

        private async Task CreateSensor(string path)
        {
            // AddSensorValueAsync awaits the queue round-trip; the sensor
            // (including template application) is fully built when the
            // await returns.
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);
        }

        private static PolicyDestinationUpdate ChatsDestination(params Guid[] chats)
        {
            var destination = new PolicyDestinationUpdate(PolicyDestinationMode.Custom);
            foreach (var chatId in chats)
                destination.Chats[chatId] = $"Chat {chatId}";

            return destination;
        }

        private static PolicyUpdate RegularUpdate(InitiatorInfo initiator, Guid? scheduleId = null,
            PolicyDestinationUpdate destination = null) => new()
        {
            ScheduleId = scheduleId,
            Destination = destination ?? new PolicyDestinationUpdate(),
            Initiator = initiator,
            ConfirmationPeriod = 0,
            Conditions =
            [
                new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value, new TargetValue(TargetType.Const, "0")),
            ],
        };

        private static PolicyUpdate TtlUpdate(InitiatorInfo initiator, TimeSpan interval, Guid? scheduleId = null,
            PolicyDestinationUpdate destination = null) => new()
        {
            TTL = interval.Ticks,
            ScheduleId = scheduleId,
            Destination = destination ?? new PolicyDestinationUpdate(),
            Initiator = initiator,
            ConfirmationPeriod = 0,
            Conditions = [],
        };
    }
}
