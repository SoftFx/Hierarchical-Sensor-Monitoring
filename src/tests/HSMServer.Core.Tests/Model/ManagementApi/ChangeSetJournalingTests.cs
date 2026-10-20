using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Folders;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Notifications.Chats;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // #1570 journaling at the service seam: every journaled write lands in
    // the REAL ChangeSets table (via the real cache and the real test
    // LevelDB) with before/after images, grouped under the client-supplied
    // changeSetId or standing alone. Dry-run writes apply nothing. The
    // rollback semantics live in the change-set administration suite.
    [Collection("Database collection")]
    public class ChangeSetJournalingTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly ClaimsPrincipal User = BuildPrincipal();

        private readonly TemplateConcurrencyFixture _fixture;

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();

        private readonly PolicyAdministrationService _policies;
        private readonly AlertTemplateAdministrationService _templates;
        private readonly AlertScheduleAdministrationService _schedules;
        private readonly IChangeSetJournal _journal;


        public ChangeSetJournalingTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
            _chats.Setup(c => c.GetValues()).Returns([]);

            var recorder = ChangeSetTestHelpers.Recorder(_databaseCoreManager.DatabaseCore);
            _journal = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);

            _policies = new PolicyAdministrationService(_valuesCache, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider, recorder);

            _templates = new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                _alertScheduleProvider, _authorization.Object, _users.Object, recorder,
                NullLogger<AlertTemplateAdministrationService>.Instance);

            var reader = new AlertReadService(_valuesCache, _alertScheduleProvider, _authorization.Object);
            _schedules = new AlertScheduleAdministrationService(_alertScheduleProvider, _valuesCache,
                _authorization.Object, reader, recorder, _users.Object);
        }


        [Fact]
        public async Task CreateSensorPolicy_JournalsBeforeAndAfterImages_AndEchoesChangeSetId()
        {
            var sensor = await CreateIntegerSensorAsync("svcJournalCreate");

            var seed = await SeedPoliciesAsync(sensor, RegularUpdate("seeded"));
            Assert.True(seed.IsOk, seed.Error);

            var changeSetId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "🔥"), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Equal(changeSetId, created.ChangeSetId);

            var stored = _journal.Get(changeSetId);

            Assert.NotNull(stored);
            Assert.Equal("api-owner", stored.Author);
            Assert.Equal(InitiatorType.User, stored.AuthorType);

            var entry = Assert.Single(stored.Entries);

            Assert.Equal(ChangeSetEntryKind.SensorDataPolicies, entry.Kind);
            Assert.Equal(ChangeSetOperation.Create, entry.Operation);
            Assert.Equal(sensor.Id, entry.EntityId);
            Assert.Equal(sensor.FullPath, entry.Path);

            // The before-image is the seeded list; the after-image adds the
            // created policy.
            var beforeId = Assert.Single(entry.Before).Id;
            var seeded = sensor.Policies.Single(p => p.Template == "seeded");

            Assert.Equal(seeded.Id, new Guid(beforeId));
            Assert.Equal(2, entry.After.Count);
            Assert.Contains(entry.After, row => new Guid(row.Id) == created.Value.Id);
        }

        [Fact]
        public async Task TwoWrites_UnderOneClientChangeSetId_GroupIntoOneChangeSet()
        {
            var sensor = await CreateIntegerSensorAsync("svcJournalGroup");
            var changeSetId = Guid.NewGuid();

            var first = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            var second = await _policies.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);

            var stored = _journal.Get(changeSetId);

            Assert.NotNull(stored);
            Assert.Equal([0, 1], stored.Entries.Select(e => e.Sequence));
            Assert.Equal(ChangeSetEntryKind.SensorDataPolicies, stored.Entries[0].Kind);
            Assert.Equal(ChangeSetEntryKind.SensorTtlPolicies, stored.Entries[1].Kind);
            Assert.Equal(ChangeSetOperation.Create, stored.Entries[1].Operation);
        }

        [Fact]
        public async Task WriteWithoutId_FormsItsOwnSingleEntryChangeSet()
        {
            var sensor = await CreateIntegerSensorAsync("svcJournalSolo");

            var first = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            var second = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);

            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);
            Assert.NotEqual(first.ChangeSetId, second.ChangeSetId);

            var firstSet = _journal.Get(first.ChangeSetId.Value);
            var secondSet = _journal.Get(second.ChangeSetId.Value);

            Assert.Single(firstSet.Entries);
            Assert.Single(secondSet.Entries);
        }

        [Fact]
        public async Task FailedWrite_JournalsNothing()
        {
            var changeSetId = Guid.NewGuid();

            var failed = await _policies.CreateSensorPolicyAsync(Guid.NewGuid(), DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.False(failed.Success);

            Assert.Null(_journal.Get(changeSetId));
        }

        [Fact]
        public async Task DryRun_AppliesNothing_AndReportsTheAffectedNode()
        {
            var sensor = await CreateIntegerSensorAsync("svcJournalDryRun");

            var result = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { DryRun = true, ChangeSetId = Guid.NewGuid() });

            Assert.True(result.Success);
            Assert.True(result.IsDryRun);
            Assert.Null(result.Value);
            Assert.NotNull(result.Preview);
            Assert.True(result.Preview.DryRun);

            // Nothing applied: the sensor's list is untouched, no entry was
            // journaled.
            Assert.Empty(sensor.Policies);
            Assert.Null(_journal.Get(Guid.Parse(result.Preview.ChangeSetId)));

            var affected = Assert.Single(result.Preview.Affected);

            Assert.Equal(sensor.Id.ToString(), affected.Id);
            Assert.Equal(sensor.FullPath, affected.Path);
        }

        [Fact]
        public async Task CreateTemplate_JournalsTemplateRowAndPerSensorFanoutImages()
        {
            var sensor = await CreateSensorAsync("svcTmplJournal", $"*/svcTmplJournal");
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(User, TemplateDto(paths: ["*/svcTmplJournal"]),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Equal(changeSetId, created.ChangeSetId);

            var entry = Assert.Single(_journal.Get(changeSetId).Entries);

            Assert.Equal(ChangeSetEntryKind.AlertTemplate, entry.Kind);
            Assert.Equal(ChangeSetOperation.Create, entry.Operation);
            Assert.Equal(created.Value.Id, entry.EntityId);
            Assert.Equal(_fixture.FolderId, entry.ParentId);
            Assert.Null(entry.BeforeTemplate);
            Assert.NotNull(entry.AfterTemplate);
            Assert.Equal(created.Value.Id, new Guid(entry.AfterTemplate.Id));

            // One fan-out image for the matched sensor: empty before-lists,
            // after-lists carrying the template-minted policies.
            var image = Assert.Single(entry.Fanout);

            Assert.Equal(sensor.Id, image.NodeId);
            Assert.Equal(ChangeSetNodeKind.Sensor, image.NodeKind);
            Assert.Empty(image.BeforePolicies);
            Assert.Empty(image.BeforeTtlPolicies);

            Assert.Contains(image.AfterPolicies, row => row.TemplateId is { Length: 16 });
            Assert.Contains(image.AfterTtlPolicies ?? [], row => row.TemplateId is { Length: 16 });
        }

        [Fact]
        public async Task DeleteTemplate_JournalsBeforeImagesOfEveryHolderSensor()
        {
            var sensor = await CreateSensorAsync("svcTmplJournalDelete", "*/svcTmplJournalDelete");

            var created = await _templates.CreateTemplateAsync(User, TemplateDto(paths: ["*/svcTmplJournalDelete"]));
            Assert.True(created.Success, created.Failure?.Message);

            var templateId = created.Value.Id;
            var changeSetId = Guid.NewGuid();

            var deleted = await _templates.DeleteTemplateAsync(templateId, User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(deleted.Success, deleted.Failure?.Message);

            var entry = Assert.Single(_journal.Get(changeSetId).Entries);

            Assert.Equal(ChangeSetOperation.Delete, entry.Operation);
            Assert.NotNull(entry.BeforeTemplate);
            Assert.Null(entry.AfterTemplate);

            var image = Assert.Single(entry.Fanout);

            Assert.Equal(sensor.Id, image.NodeId);
            Assert.Contains(image.BeforePolicies, row => row.TemplateId is { Length: 16 } template && new Guid(template) == templateId);

            // The strip removed them: the after-lists carry no policy of
            // this template.
            Assert.DoesNotContain(image.AfterPolicies, row => row.TemplateId is { Length: 16 } template && new Guid(template) == templateId);
        }

        [Fact]
        public async Task CreateSchedule_JournalsScheduleRow()
        {
            var changeSetId = Guid.NewGuid();

            var created = await _schedules.CreateScheduleAsync(User, ScheduleDto(),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(created.Success, created.Failure?.Message);
            Assert.Equal(changeSetId, created.ChangeSetId);

            var entry = Assert.Single(_journal.Get(changeSetId).Entries);

            Assert.Equal(ChangeSetEntryKind.AlertSchedule, entry.Kind);
            Assert.Equal(ChangeSetOperation.Create, entry.Operation);
            Assert.Null(entry.BeforeSchedule);
            Assert.Equal(created.Value.Id, new Guid(entry.AfterSchedule.Id));
        }

        [Fact]
        public async Task FailedJournalAppend_WriteSucceedsWithoutEchoingADeadChangeSetId()
        {
            // Record's false = the undo point is lost, but the write itself
            // already applied. The template/schedule engines stamp the echoed
            // changeSetId ONLY on true (the policy engine's rule): a client
            // must never receive an undo handle that does not exist.
            var failing = new ChangeSetRecorder(new ThrowingJournal(),
                NullLogger<ChangeSetRecorder>.Instance);

            var schedules = new AlertScheduleAdministrationService(_alertScheduleProvider, _valuesCache,
                _authorization.Object, new AlertReadService(_valuesCache, _alertScheduleProvider, _authorization.Object),
                failing, _users.Object);
            var templates = new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                _alertScheduleProvider, _authorization.Object, _users.Object, failing,
                NullLogger<AlertTemplateAdministrationService>.Instance);

            var schedule = await schedules.CreateScheduleAsync(User, ScheduleDto(),
                new ChangeSetWriteOptions { ChangeSetId = Guid.NewGuid() });
            Assert.True(schedule.Success, schedule.Failure?.Message);
            Assert.Null(schedule.ChangeSetId);

            var sensor = await CreateIntegerSensorAsync("svcJournalLost");
            var createdTemplate = await templates.CreateTemplateAsync(User, TemplateDto(paths: ["*/svcJournalLost"]));
            Assert.True(createdTemplate.Success, createdTemplate.Failure?.Message);
            Assert.Null(createdTemplate.ChangeSetId);

            var deletedTemplate = await templates.DeleteTemplateAsync(createdTemplate.Value.Id, User);
            Assert.True(deletedTemplate.Success, deletedTemplate.Failure?.Message);
            Assert.Null(deletedTemplate.ChangeSetId);
        }


        // === Test vocabulary (mirrors PolicyAdministrationServiceTests) ===

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static InitiatorInfo Force { get; } = InitiatorInfo.AsSystemForce("change_set_journaling_tests");

        private async Task<Core.Model.BaseSensorModel> CreateSensorAsync(string path, string wildcard = null)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            return sensor;
        }

        private Task<Core.Model.BaseSensorModel> CreateIntegerSensorAsync(string name) =>
            CreateSensorAsync($"ProductA_concurrency/{name}");

        private Task<TaskResult> SeedPoliciesAsync(Core.Model.BaseSensorModel sensor, params PolicyUpdate[] policies) =>
            _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                Policies = [.. policies],
            });

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
                    Target = JsonSerializer.SerializeToElement(20),
                },
            ],
            Notification = new AlertNotificationDto { Template = "api [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private AlertTemplateDto TemplateDto(string[] paths) => new()
        {
            Name = $"template-{Guid.NewGuid():N}",
            SensorType = (byte)SensorType.Integer,
            FolderId = _fixture.FolderId,
            Paths = [.. paths],
            Policies =
            [
                new AlertPolicyDto
                {
                    Conditions =
                    [
                        new PolicyConditionDto
                        {
                            Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "42" },
                            Combination = (byte)PolicyCombination.And,
                            Operation = (byte)PolicyOperation.GreaterThan,
                            Property = (byte)PolicyProperty.Value,
                        },
                    ],
                    SensorStatus = (byte)SensorStatus.Ok,
                },
            ],
            TtlPolicies = [new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok }],
            Ttls = [new TimeIntervalDto { Interval = (long)CoreTimeInterval.Ticks, Ticks = TimeSpan.FromMinutes(1).Ticks }],
        };

        private static TtlPolicyDto TtlPolicy() => new()
        {
            Interval = "00:30:00",
            Notification = new AlertNotificationDto { Template = "api ttl [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private static AlertScheduleUpsertDto ScheduleDto() => new()
        {
            Name = $"schedule-{Guid.NewGuid():N}",
            Timezone = "UTC",
            Schedule = """
                daySchedules:
                  - days: [mon, tue, wed, thu, fri]
                    windows:
                      - start: 09:00
                        end: 18:00
                """,
        };


        // A journal whose storage is down: every Append throws, surfacing
        // Record's null path without tearing down the real test database.
        private sealed class ThrowingJournal : IChangeSetJournal
        {
            public ChangeSetModel Get(Guid changeSetId) => throw new NotImplementedException();

            public List<ChangeSetModel> GetAll() => throw new NotImplementedException();

            public Guid Append(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry) =>
                throw new InvalidOperationException("the journal storage is down");

            public void MarkRolledBack(Guid changeSetId, Guid rollbackChangeSetId) => throw new NotImplementedException();

            public void MarkEntryRestored(Guid changeSetId, int entrySequence, Guid rollbackChangeSetId) => throw new NotImplementedException();

            public int PurgeReferences(Guid entityId, Guid? folderId = null) => throw new NotImplementedException();

            public int PurgeReferences(IReadOnlyCollection<Guid> entityIds, Guid? folderId = null) => throw new NotImplementedException();
        }
    }
}
