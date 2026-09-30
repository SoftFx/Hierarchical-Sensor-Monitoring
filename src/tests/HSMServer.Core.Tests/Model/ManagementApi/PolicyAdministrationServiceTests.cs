using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Notifications.Chats;
using HSMServer.Folders;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // Merge semantics of the alert-administration write engine (#1500) at the
    // service seam: every write is ONE item change merged into the node's FULL
    // policy list and applied through the real cache — the same full-list
    // machinery the web editor drives. The suites pin the two ways a merge could
    // silently lose state: dropping sibling policies (a partial list) and
    // resetting sibling TTL intervals (a null TTL is an explicit reset-to-parent,
    // the #1409/#1451 lesson), plus the product aggregate's routing and the
    // template-ownership guards.
    [Collection("Database collection")]
    public class PolicyAdministrationServiceTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly ClaimsPrincipal User = BuildPrincipal();

        private readonly TemplateConcurrencyFixture _fixture;

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();

        private readonly PolicyAdministrationService _service;


        public PolicyAdministrationServiceTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));

            _chats.Setup(c => c.GetValues()).Returns([]);

            _service = new PolicyAdministrationService(_valuesCache, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider);
        }


        // === Slice A: data-policy merge on a sensor ===

        [Fact]
        public async Task CreateSensorPolicy_AppendsWithoutTouchingSiblingPolicies()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeCreate");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("first"), RegularUpdate("second"));
            Assert.True(seed.IsOk, seed.Error);

            var created = await _service.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "🔥"), User);

            Assert.True(created.Success, created.Failure?.Message);

            // BOTH seeded policies ride through with their content intact, the
            // new one lands: 3 total.
            Assert.Equal(3, sensor.Policies.Count());

            var first = sensor.Policies.Single(p => p.Template == "first");
            var second = sensor.Policies.Single(p => p.Template == "second");

            Assert.Equal("first-icon", first.Icon);
            Assert.Equal("second-icon", second.Icon);
            Assert.Equal(PolicyOperation.GreaterThan, first.Conditions.Single().Operation);

            // The created policy reads back with the requested content.
            Assert.Equal("🔥", sensor.Policies.Single(p => p.Id == created.Value.Id).Icon);
            Assert.True(sensor.Policies.Single(p => p.Id == created.Value.Id).Conditions.Single().Operation
                is PolicyOperation.GreaterThan);
        }

        [Fact]
        public async Task CreateSensorPolicy_DoesNotTouchTtlPolicies()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeCreateTtl");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies = [TtlUpdate(Force, TimeSpan.FromMinutes(30))],
            });
            Assert.True(seed.IsOk, seed.Error);

            var created = await _service.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);

            Assert.True(created.Success, created.Failure?.Message);

            // The data-policy merge re-asserts ONLY the data list: the TTL
            // policy and its explicit interval are untouched.
            var ttl = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, ttl.TTLInterval.Ticks);
            Assert.False(ttl.IsTTLFromParent);
        }

        [Fact]
        public async Task UpdateSensorPolicy_ReplacesTargetAndLeavesSiblingsUntouched()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeUpdate");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("keep-me"), RegularUpdate("edit-me"));
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.Template == "edit-me");

            var updated = await _service.UpdateSensorPolicyAsync(sensor.Id, target.Id,
                DataPolicy(icon: "edited"), User);

            Assert.True(updated.Success, updated.Failure?.Message);

            Assert.Equal(2, sensor.Policies.Count());

            var untouched = sensor.Policies.Single(p => p.Template == "keep-me");
            Assert.Equal("keep-me-icon", untouched.Icon);
            Assert.NotEqual(target.Id, untouched.Id);

            // The target kept its ID and took the new content.
            var edited = sensor.Policies.Single(p => p.Id == target.Id);
            Assert.Equal("edited", edited.Icon);
        }

        [Fact]
        public async Task DeleteSensorPolicy_RemovesOnlyTheTarget()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeDelete");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("first"), RegularUpdate("second"));
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.Template == "second");

            var deleted = await _service.DeleteSensorPolicyAsync(sensor.Id, target.Id, User);

            Assert.True(deleted.Success, deleted.Failure?.Message);

            var survivor = Assert.Single(sensor.Policies);
            Assert.Equal("first", survivor.Template);
            Assert.Equal("first-icon", survivor.Icon);
        }

        [Fact]
        public async Task UpdateSensorPolicy_TemplateOwnedPolicy_ContentChangeAnswersConflict()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeTemplateGuard");

            var seed = await SeedPoliciesAsync(sensor,
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    Template = "template-made",
                    Destination = new PolicyDestinationUpdate(),
                    Icon = "tpl-icon",
                    TemplateId = Guid.NewGuid(),
                    Conditions =
                    [
                        new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                            new TargetValue(TargetType.Const, "10")),
                    ],
                    Initiator = Force,
                });
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.TemplateId is not null);

            // Content change on a template-owned policy: 409, not a silent no-op.
            var conflict = await _service.UpdateSensorPolicyAsync(sensor.Id, target.Id, DataPolicy(icon: "hacked"), User);

            Assert.Equal(PolicyWriteOutcome.Conflict, conflict.Failure.Outcome);
            Assert.Equal("tpl-icon", sensor.Policies.Single(p => p.Id == target.Id).Icon);

            // The disable toggle alone is allowed through.
            var toggle = await _service.UpdateSensorPolicyAsync(sensor.Id, target.Id, TemplateOwnedToggle(target), User);

            Assert.True(toggle.Success, toggle.Failure?.Message);

            var toggled = sensor.Policies.Single(p => p.Id == target.Id);
            Assert.True(toggled.IsDisabled);

            // The toggle rides the STORED content — template linkage included
            // (server-owned fields, #1501 round-1).
            Assert.Equal(target.TemplateId, toggled.TemplateId);
            Assert.Equal("tpl-icon", toggled.Icon);
        }

        [Fact]
        public async Task DeleteSensorPolicy_TemplateOwnedPolicy_AnswersConflict()
        {
            var sensor = await CreateIntegerSensorAsync("svcMergeTemplateDelete");

            var templateId = Guid.NewGuid();

            var seed = await SeedPoliciesAsync(sensor,
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    Template = "template-made",
                    Destination = new PolicyDestinationUpdate(),
                    TemplateId = templateId,
                    Conditions =
                    [
                        new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                            new TargetValue(TargetType.Const, "10")),
                    ],
                    Initiator = Force,
                });
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.TemplateId == templateId);

            var deleted = await _service.DeleteSensorPolicyAsync(sensor.Id, target.Id, User);

            // The core PRESERVES template-owned policies on a user-initiated
            // full-list drop; the API reports the same as 409 instead of a
            // phantom 204.
            Assert.Equal(PolicyWriteOutcome.Conflict, deleted.Failure.Outcome);
            Assert.NotNull(sensor.Policies.SingleOrDefault(p => p.Id == target.Id));
        }


        // === Template fields are server-owned (#1501 round-1, F1) ===

        [Fact]
        public async Task CreateSensorPolicy_TemplateIdInBody_IsIgnored_CreateSucceeds()
        {
            var sensor = await CreateIntegerSensorAsync("svcCreateTemplateField");

            // A body claiming template ownership must NOT hit the core's add
            // gate (which would surface as a misleading 409): the field is
            // server-owned and forced to null on create.
            var created = await _service.CreateSensorPolicyAsync(sensor.Id,
                DataPolicy() with { TemplateId = Guid.NewGuid(), TemplateAlertId = Guid.NewGuid() }, User);

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Null(created.Value.TemplateId);
            Assert.Null(created.Value.TemplateAlertId);
            Assert.Null(sensor.Policies.Single(p => p.Id == created.Value.Id).TemplateId);
        }

        [Fact]
        public async Task CreateSensorTtlPolicy_TemplateIdInBody_IsIgnored_CreateSucceeds()
        {
            var sensor = await CreateIntegerSensorAsync("svcCreateTtlTemplateField");

            var created = await _service.CreateSensorTtlPolicyAsync(sensor.Id,
                TtlPolicy("00:30:00") with { TemplateId = Guid.NewGuid(), TemplateAlertId = Guid.NewGuid() }, User);

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Null(created.Value.TemplateId);
            Assert.Null(sensor.Policies.TTLPolicies.Single(p => p.Id == created.Value.Id).TemplateId);
        }

        [Fact]
        public async Task UpdateSensorPolicy_TemplateIdInBody_IsIgnored_PolicyStaysUserOwned()
        {
            var sensor = await CreateIntegerSensorAsync("svcUpdateTemplateField");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("user-made"));
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.Template == "user-made");

            // A PATCH must not be able to mint template ownership either — the
            // stored linkage (null for a user policy) is carried, not the body's.
            var updated = await _service.UpdateSensorPolicyAsync(sensor.Id, target.Id,
                DataPolicy() with { TemplateId = Guid.NewGuid(), TemplateAlertId = Guid.NewGuid() }, User);

            Assert.True(updated.Success, updated.Failure?.Message);

            var stored = sensor.Policies.Single(p => p.Id == target.Id);
            Assert.Null(stored.TemplateId);
            Assert.Null(stored.TemplateAlertId);
        }

        [Fact]
        public async Task UpdateSensorPolicy_TemplateOwnedToggle_OutOfSurfaceContent_SkipsContentValidation()
        {
            var sensor = await CreateIntegerSensorAsync("svcTemplateToggleOutOfSurface");

            var templateId = Guid.NewGuid();

            // A template-minted condition OUTSIDE the API's write surface: a
            // non-Const (LastValue) target, which reads back as target: null —
            // an echoed body can never pass TryBuildConditions ("GreaterThan
            // requires a target").
            var seed = await SeedPoliciesAsync(sensor,
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    Template = "template-made",
                    Destination = new PolicyDestinationUpdate(),
                    Icon = "tpl-icon",
                    TemplateId = templateId,
                    Conditions =
                    [
                        new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                            new TargetValue(TargetType.LastValue, sensor.Id.ToString())),
                    ],
                    Initiator = Force,
                });
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.TemplateId == templateId);

            // The GET-echo toggle body: the stored content as it READS (target
            // null), isDisabled flipped. The core only applies the toggle for
            // user-initiated template-policy updates, so validation would check
            // content that is never written — it is skipped.
            var toggled = await _service.UpdateSensorPolicyAsync(sensor.Id, target.Id, new PolicyDto
            {
                IsDisabled = true,
                Conditions =
                [
                    new AlertConditionDto { Property = "Value", Operation = "GreaterThan", Target = null },
                ],
                Notification = new AlertNotificationDto { Template = "template-made" },
                Destination = new AlertDestinationDto { Mode = target.Destination.Mode.ToString() },
                Icon = "tpl-icon",
            }, User);

            Assert.True(toggled.Success, toggled.Failure?.Message);
            Assert.True(toggled.Value.IsDisabled);

            var stored = sensor.Policies.Single(p => p.Id == target.Id);
            Assert.True(stored.IsDisabled);

            // The stored content rides through untouched — linkage included.
            Assert.Equal(templateId, stored.TemplateId);
            Assert.Equal("tpl-icon", stored.Icon);
            Assert.Equal(TargetType.LastValue, stored.Conditions.Single().Target.Type);
        }

        [Fact]
        public async Task CreateSensorPolicy_ConcurrentCreatesOnOneSensor_BothPoliciesSurvive()
        {
            var sensor = await CreateIntegerSensorAsync("svcConcurrentCreate");

            // Two concurrent full-list merges built from the same snapshot would
            // acknowledge both while the second list drops the first policy —
            // the per-node write gate serializes them (#1501 round-1, F2).
            var results = await Task.WhenAll(
                Task.Run(() => _service.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "one"), User)),
                Task.Run(() => _service.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "two"), User)));

            Assert.All(results, result => Assert.True(result.Success, result.Failure?.Message));

            Assert.Equal(2, sensor.Policies.Count());
            Assert.NotNull(sensor.Policies.SingleOrDefault(p => p.Id == results[0].Value.Id));
            Assert.NotNull(sensor.Policies.SingleOrDefault(p => p.Id == results[1].Value.Id));
        }


        // === Slice C: TTL policies on a sensor ===

        [Fact]
        public async Task CreateSensorTtlPolicy_SetsExplicitInterval_AndPreservesSiblings()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlCreate");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies = [TtlUpdate(Force, TimeSpan.FromMinutes(30))],
            });
            Assert.True(seed.IsOk, seed.Error);

            var created = await _service.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy("01:00:00"), User);

            Assert.True(created.Success, created.Failure?.Message);

            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);

            // The sibling keeps its EXPLICIT interval — the merge re-asserts it
            // (a null TTL would be an explicit reset-to-parent).
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks,
                sensor.Policies.TTLPolicies.Single(p => p.Id != created.Value.Id).TTLInterval.Ticks);

            var createdPolicy = sensor.Policies.TTLPolicies.Single(p => p.Id == created.Value.Id);
            Assert.Equal(TimeSpan.FromHours(1).Ticks, createdPolicy.TTLInterval.Ticks);
            Assert.False(createdPolicy.IsTTLFromParent);
        }

        [Fact]
        public async Task UpdateSensorTtlPolicy_InheritSwitch_ResetsToParent()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlInherit");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies = [TtlUpdate(Force, TimeSpan.FromMinutes(30))],
            });
            Assert.True(seed.IsOk, seed.Error);

            var target = Assert.Single(sensor.Policies.TTLPolicies);

            var updated = await _service.UpdateSensorTtlPolicyAsync(sensor.Id, target.Id, TtlPolicy(inherit: true), User);

            Assert.True(updated.Success, updated.Failure?.Message);
            Assert.True(updated.Value.Inherit);
            Assert.Null(updated.Value.Interval);

            // The STORED policy flipped to from-parent (a null TTL in full-list
            // semantics is the explicit reset — the server translated the
            // inherit switch, the client never sent a null).
            var stored = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.True(stored.IsTTLFromParent);
        }

        [Fact]
        public async Task UpdateSensorTtlPolicy_OtherTtlIntervalsPreserved()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlSiblingKeep");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies =
                [
                    TtlUpdate(Force, TimeSpan.FromMinutes(30)),
                    TtlUpdate(Force, TimeSpan.FromMinutes(45)),
                ],
            });
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.TTLPolicies.Single(p => p.TTLInterval.Ticks == TimeSpan.FromMinutes(30).Ticks);

            var updated = await _service.UpdateSensorTtlPolicyAsync(sensor.Id, target.Id, TtlPolicy("02:00:00"), User);

            Assert.True(updated.Success, updated.Failure?.Message);

            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);
            Assert.Equal(TimeSpan.FromHours(2).Ticks, sensor.Policies.TTLPolicies.Single(p => p.Id == target.Id).TTLInterval.Ticks);

            // The untouched sibling keeps its interval — the full-list merge
            // re-asserts it explicitly.
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks,
                sensor.Policies.TTLPolicies.Single(p => p.Id != target.Id).TTLInterval.Ticks);
        }

        [Fact]
        public async Task DeleteSensorTtlPolicy_RemovesOnlyTheTarget()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlDelete");

            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies =
                [
                    TtlUpdate(Force, TimeSpan.FromMinutes(30)),
                    TtlUpdate(Force, TimeSpan.FromMinutes(45)),
                ],
            });
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.TTLPolicies.Single(p => p.TTLInterval.Ticks == TimeSpan.FromMinutes(30).Ticks);

            var deleted = await _service.DeleteSensorTtlPolicyAsync(sensor.Id, target.Id, User);

            Assert.True(deleted.Success, deleted.Failure?.Message);

            var survivor = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(TimeSpan.FromMinutes(45).Ticks, survivor.TTLInterval.Ticks);
        }

        [Fact]
        public async Task CreateSensorTtlPolicy_MissingIntervalAndInherit_AnswersInvalid()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlValidation");

            var created = await _service.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy(interval: null, inherit: false), User);

            Assert.Equal(PolicyWriteOutcome.Invalid, created.Failure.Outcome);
            Assert.Empty(sensor.Policies.TTLPolicies);
        }

        // #1501 round-2 (F2): a full-list TTL write must not RE-STAMP the
        // change-table ownership of the siblings it merely re-asserts — the
        // TTL stamp loop in BaseNodeModel.Update stamps unconditionally per
        // id, so without PreserveChangeOwnership one API call would claim a
        // template-applied sibling as the calling user and the NEXT template
        // apply (AlertTemplate, type 15) would fail the node's CanChange
        // pre-check (100 <= 15 is false) for the whole node.
        [Fact]
        public async Task CreateSensorTtlPolicy_DoesNotRestampSiblingTtlOwnership_TemplateReapplyStillLands()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlSiblingOwnership");

            var templateId = Guid.NewGuid();

            // A template-applied TTL sibling, seeded the way a template apply
            // lands it: an AlertTemplate-initiated full-list update carrying a
            // non-empty id (the stamp loop skips empty ids on creation), which
            // stamps the change-table owner as AlertTemplate.
            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AlertTemplate,
                TTLPolicies = [TtlUpdate(InitiatorInfo.AlertTemplate, TimeSpan.FromMinutes(30)) with { Id = Guid.NewGuid(), TemplateId = templateId }],
            });
            Assert.True(seed.IsOk, seed.Error);

            var sibling = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(InitiatorType.AlertTemplate, sensor.ChangeTable.TtlPolicies[sibling.Id.ToString()].Initiator.Type);

            // An API write of an UNRELATED TTL policy re-asserts the sibling...
            var created = await _service.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy("01:00:00"), User);

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);

            // ...WITHOUT re-stamping its change-table owner: a re-assert is not
            // an edit. (The CHANGED item keeps normal stamping — the new
            // policy is owned by the API user.)
            Assert.Equal(InitiatorType.AlertTemplate, sensor.ChangeTable.TtlPolicies[sibling.Id.ToString()].Initiator.Type);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[created.Value.Id.ToString()].Initiator.Type);

            // Consequence, pinned end-to-end: the next template apply — the
            // same AlertTemplate full-list update shape
            // UpdateTemplateSensorAlerts sends — still lands on the node
            // (the sibling takes the template's new interval).
            var reapply = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AlertTemplate,
                TTLPolicies = [TtlUpdate(InitiatorInfo.AlertTemplate, TimeSpan.FromMinutes(99)) with { Id = sibling.Id, TemplateId = templateId }],
            });
            Assert.True(reapply.IsOk, reapply.Error);

            Assert.Equal(TimeSpan.FromMinutes(99).Ticks, sibling.TTLInterval.Ticks);
        }

        [Fact]
        public async Task UpdateSensorTtlPolicy_TemplateOwnedToggle_NoEffectEcho_SkipsContentValidation()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlTemplateToggle");

            var templateId = Guid.NewGuid();

            // A template-minted TTL policy with NO effect at all (no
            // notification, no icon) — inside the core's range, outside the
            // API's write surface ("at least one effect" would 422 the echo).
            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                TTLPolicies = [TtlUpdate(Force, TimeSpan.FromMinutes(30)) with { TemplateId = templateId }],
            });
            Assert.True(seed.IsOk, seed.Error);

            var target = Assert.Single(sensor.Policies.TTLPolicies);

            // The GET-echo toggle body: interval and destination as read, no
            // effect, isDisabled flipped — applied without content validation
            // (the core applies only the toggle for template-owned TTL).
            var toggled = await _service.UpdateSensorTtlPolicyAsync(sensor.Id, target.Id, new TtlPolicyDto
            {
                IsDisabled = true,
                Interval = "00:30:00",
                Destination = new AlertDestinationDto { Mode = target.Destination.Mode.ToString() },
            }, User);

            Assert.True(toggled.Success, toggled.Failure?.Message);
            Assert.True(toggled.Value.IsDisabled);

            var stored = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.True(stored.IsDisabled);

            // The stored interval and linkage ride through untouched.
            Assert.False(stored.IsTTLFromParent);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, stored.TTLInterval.Ticks);
            Assert.Equal(templateId, stored.TemplateId);
        }

        // #1501 round-3 (F3): the template-owned TTL toggle is the user's
        // CHANGE — the changed item takes NORMAL change ownership (no
        // PreserveChangeOwnership), so the change table records the API user
        // as the owner; the untouched siblings keep theirs (round-2, F2).
        [Fact]
        public async Task UpdateSensorTtlPolicy_TemplateOwnedToggle_StampsTheUserAsOwner_AndProtectsTheDisable()
        {
            var sensor = await CreateIntegerSensorAsync("svcTtlToggleOwnership");

            var templateId = Guid.NewGuid();

            // A template-applied TTL policy, seeded the way a template apply
            // lands it (AlertTemplate initiator, non-empty id) — owner
            // AlertTemplate.
            var seed = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AlertTemplate,
                TTLPolicies = [TtlUpdate(InitiatorInfo.AlertTemplate, TimeSpan.FromMinutes(30)) with { Id = Guid.NewGuid(), TemplateId = templateId }],
            });
            Assert.True(seed.IsOk, seed.Error);

            var target = Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(InitiatorType.AlertTemplate, sensor.ChangeTable.TtlPolicies[target.Id.ToString()].Initiator.Type);

            var toggled = await _service.UpdateSensorTtlPolicyAsync(sensor.Id, target.Id, new TtlPolicyDto
            {
                IsDisabled = true,
                Interval = "00:30:00",
                Destination = new AlertDestinationDto { Mode = target.Destination.Mode.ToString() },
            }, User);

            Assert.True(toggled.Success, toggled.Failure?.Message);

            var stored = Assert.Single(sensor.Policies.TTLPolicies);

            Assert.True(stored.IsDisabled);

            // The toggle TOOK the ownership: the API user (not the template)
            // owns the policy's change-table entry now.
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[target.Id.ToString()].Initiator.Type);

            // Consequence, pinned end-to-end: the next template apply
            // (AlertTemplate, type 15) fails the node's CanChange pre-check
            // (owner User 100 > 15) and the user's disable survives it —
            // before the fix the un-stamped toggle let the template overwrite
            // the disable without the pre-check noticing.
            var reapply = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AlertTemplate,
                TTLPolicies = [TtlUpdate(InitiatorInfo.AlertTemplate, TimeSpan.FromMinutes(99)) with { Id = target.Id, TemplateId = templateId, IsDisabled = false }],
            });
            Assert.True(reapply.IsOk, reapply.Error);

            stored = Assert.Single(sensor.Policies.TTLPolicies);

            Assert.True(stored.IsDisabled);
            Assert.Equal(TimeSpan.FromMinutes(30).Ticks, stored.TTLInterval.Ticks);
        }


        // === Slice D: products ===

        [Fact]
        public async Task CreateProductPolicy_AlwaysAnswersInvalid_PointingAtTheSensorEndpoint()
        {
            var result = await _service.CreateProductPolicyAsync(_fixture.ProductAId, DataPolicy(), User);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("sensor", result.Failure.Errors["policies"].Single());
        }

        [Fact]
        public async Task UpdateProductPolicy_RoutesTheChangeToTheOwningSensor()
        {
            var sensor = await CreateIntegerSensorAsync("svcProductRoute");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("product-routed"));
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.Template == "product-routed");

            var updated = await _service.UpdateProductPolicyAsync(_fixture.ProductAId, target.Id, DataPolicy(icon: "via-product"), User);

            Assert.True(updated.Success, updated.Failure?.Message);
            Assert.Equal(sensor.Id, updated.Value.SensorId);
            Assert.Equal("via-product", sensor.Policies.Single(p => p.Id == target.Id).Icon);
        }

        [Fact]
        public async Task UpdateProductPolicy_UnknownPolicyId_AnswersNotFound()
        {
            await CreateIntegerSensorAsync("svcProductUnknown");

            var result = await _service.UpdateProductPolicyAsync(_fixture.ProductAId, Guid.NewGuid(), DataPolicy(), User);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task DeleteProductPolicy_RoutesToTheOwningSensor_AndLeavesSiblingsIntact()
        {
            var sensor = await CreateIntegerSensorAsync("svcProductDelete");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("first"), RegularUpdate("via-product"));
            Assert.True(seed.IsOk, seed.Error);

            var target = sensor.Policies.Single(p => p.Template == "via-product");

            var deleted = await _service.DeleteProductPolicyAsync(_fixture.ProductAId, target.Id, User);

            Assert.True(deleted.Success, deleted.Failure?.Message);

            var survivor = Assert.Single(sensor.Policies);
            Assert.Equal("first", survivor.Template);
        }

        [Fact]
        public async Task ProductTtlPolicies_FullCrud_MergesWithoutLoss()
        {
            var seed = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = _fixture.ProductAId,
                Initiator = Force,
                TTLPolicies = [TtlUpdate(Force, TimeSpan.FromMinutes(15))],
            }, default);
            Assert.True(seed.IsOk, seed.Error);

            var created = await _service.CreateProductTtlPolicyAsync(_fixture.ProductAId, TtlPolicy("03:00:00"), User);

            Assert.True(created.Success, created.Failure?.Message);

            var product = _valuesCache.GetProduct(_fixture.ProductAId);

            Assert.Equal(2, product.Policies.TTLPolicies.Count);
            Assert.Equal(TimeSpan.FromMinutes(15).Ticks,
                product.Policies.TTLPolicies.Single(p => p.Id != created.Value.Id).TTLInterval.Ticks);

            var updated = await _service.UpdateProductTtlPolicyAsync(_fixture.ProductAId, created.Value.Id,
                TtlPolicy(inherit: true), User);

            Assert.True(updated.Success, updated.Failure?.Message);
            Assert.True(product.Policies.TTLPolicies.Single(p => p.Id == created.Value.Id).IsTTLFromParent);

            var deleted = await _service.DeleteProductTtlPolicyAsync(_fixture.ProductAId, created.Value.Id, User);

            Assert.True(deleted.Success, deleted.Failure?.Message);
            var survivor = Assert.Single(product.Policies.TTLPolicies);
            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, survivor.TTLInterval.Ticks);
        }


        // === Slice E: write-gate lifecycle (#1501 round-2, F3) ===

        // A write for an unknown id answers 404 BEFORE any gate exists: a
        // read-write token spraying random GUIDs must not leak one gate per
        // id (each answered 404, none ever removed).
        [Fact]
        public async Task WriteGate_UnknownSensor404_AllocatesNoGate()
        {
            var result = await _service.CreateSensorPolicyAsync(Guid.NewGuid(), DataPolicy(), User);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            Assert.Equal(0, _service.WriteGateCount);
        }

        [Fact]
        public async Task WriteGate_ForbiddenWrite_AllocatesNoGate()
        {
            var sensor = await CreateIntegerSensorAsync("svcGateForbidden");

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await _service.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy("00:30:00"), User);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal(0, _service.WriteGateCount);
        }

        // The registry is bounded: an idle gate (nobody holding or waiting)
        // leaves when its last user does — the count is 0 again after a
        // completed write, not one per node ever written.
        [Fact]
        public async Task WriteGate_IdleGateIsRemovedAfterTheWrite()
        {
            var sensor = await CreateIntegerSensorAsync("svcGateIdle");

            var created = await _service.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Equal(0, _service.WriteGateCount);
        }


        // === Test vocabulary ===

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static InitiatorInfo Force { get; } = InitiatorInfo.AsSystemForce("policy_administration_tests");

        private async Task<Core.Model.BaseSensorModel> CreateIntegerSensorAsync(string name)
        {
            var path = $"ProductA_concurrency/{name}";

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            return sensor;
        }

        private Task<TaskResult> SeedPoliciesAsync(Core.Model.BaseSensorModel sensor, params PolicyUpdate[] policies) =>
            _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                Policies = [.. policies],
            });

        // The TTL seeding shape of ChatRemovalPreservationTests: an explicit
        // interval, no schedule binding, default destination.
        private static PolicyUpdate TtlUpdate(InitiatorInfo initiator, TimeSpan interval) => new()
        {
            TTL = interval.Ticks,
            Initiator = initiator,
            Destination = new PolicyDestinationUpdate(),
            Conditions = [],
        };

        // A manual regular policy shaped like the ChatRemovalPreservationTests'
        // RegularUpdate helper: an integer Value condition, an icon, a template
        // name that doubles as the assertion key. Destination is REQUIRED — a
        // null destination update NREs inside Policy.TryUpdate and the policy
        // silently never lands.
        private static PolicyUpdate RegularUpdate(string template) => new()
        {
            Id = Guid.NewGuid(),
            Template = template,
            Icon = $"{template}-icon",
            Destination = new PolicyDestinationUpdate(),
            Conditions =
            [
                new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                    new TargetValue(TargetType.Const, "10")),
            ],
            Initiator = Force,
        };

        private static PolicyDto DataPolicy(string icon = null) => new()
        {
            Icon = icon,
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    // The wire value is a typed JSON value; tests build it the
                    // way the model binder would.
                    Target = JsonSerializer.SerializeToElement(20),
                },
            ],
            Notification = new AlertNotificationDto { Template = "api [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        // The disable-toggle-only body for a template-owned policy: the same
        // content the mapper renders for the stored policy, IsDisabled flipped.
        private static PolicyDto TemplateOwnedToggle(Policy stored) => new()
        {
            IsDisabled = true,
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = stored.Conditions[0].Property.ToString(),
                    Operation = stored.Conditions[0].Operation.ToString(),
                    Target = JsonSerializer.SerializeToElement(10),
                },
            ],
            Notification = new AlertNotificationDto { Template = stored.Template },
            Destination = new AlertDestinationDto { Mode = stored.Destination.Mode.ToString() },
            Icon = stored.Icon,
        };

        private static TtlPolicyDto TtlPolicy(string interval = null, bool inherit = false) => new()
        {
            Interval = interval,
            Inherit = inherit,
            Notification = new AlertNotificationDto { Template = "api ttl [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };
    }
}
