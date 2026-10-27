using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Journal;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // #1588: the unified Journal surface — every configuration write from
    // every source (REST, MCP and the web editor's single write path) leaves
    // a kind-stamped journal record linked to its request's undo group; the
    // journal service lists/filters them and rolls back BY RECORD, admin
    // only. The undo mechanics themselves stay covered by the change-set
    // suites; these tests pin the JOURNAL-side contract.
    [Collection("Database collection")]
    public class JournalAdministrationServiceTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly ClaimsPrincipal TokenUser = BuildPrincipal();

        private readonly TemplateConcurrencyFixture _fixture;

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();

        private readonly User _owner = new("api-owner") { IsAdmin = true };

        private readonly PolicyAdministrationService _policies;
        private readonly AlertScheduleAdministrationService _schedules;
        private readonly JournalAdministrationService _journal;
        private readonly JournalService _journalService;


        public JournalAdministrationServiceTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(() => _owner);
            _chats.Setup(c => c.GetValues()).Returns([]);

            var changeSets = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);
            _journalService = new JournalService(_databaseCoreManager.DatabaseCore);
            var recorder = new ChangeSetRecorder(changeSets, _journalService,
                NullLogger<ChangeSetRecorder>.Instance);

            _policies = new PolicyAdministrationService(_valuesCache, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider, recorder);

            var templates = new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                _alertScheduleProvider, _authorization.Object, _users.Object, recorder,
                NullLogger<AlertTemplateAdministrationService>.Instance);

            var reader = new AlertReadService(_valuesCache, _alertScheduleProvider, _authorization.Object);
            _schedules = new AlertScheduleAdministrationService(_alertScheduleProvider, _valuesCache,
                _authorization.Object, reader, recorder, _users.Object);

            var engine = new ChangeSetAdministrationService(changeSets, _valuesCache, _alertScheduleProvider,
                _authorization.Object, _users.Object, _policies, templates, _schedules, recorder);

            _journal = new JournalAdministrationService(_journalService, _valuesCache, _folders.Object,
                _authorization.Object, _users.Object, engine);
        }


        [Fact]
        public async Task PolicyWrite_LeavesAKindStampedRecordLinkedToTheRequestGroup()
        {
            var sensor = await CreateSensorAsync("jrnPolicy");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal(JournalKinds.SensorDataPolicies, record.Kind);
            Assert.Equal(changeSetId.ToString(), record.ChangeSetId);
            Assert.Equal(_fixture.ProductAId.ToString(), record.ProductId);
            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task WebEditorFullListWrite_JournalsBothLists_UnderOneRequestGroup()
        {
            // The single write path (#1588): the web editor's full-list save
            // rides the policy administration service, so both lists journal
            // under the post's request group — exactly like a REST write.
            var sensor = await CreateSensorAsync("jrnWebFullList");
            var changeSetId = Guid.NewGuid();
            var initiator = InitiatorInfo.AsUser("web-user");

            // The web form re-asserts the full list: the seeded policy plus
            // nothing else (full-list semantics), like the editor's save.
            var seeded = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser);
            Assert.True(seeded.Success, seeded.Failure?.Message);

            var fullList = sensor.Policies.Select(p => new PolicyUpdate(p, initiator)).ToList();

            var write = await _policies.WriteSensorListsAsync(sensor.Id,
                dataPolicies: fullList,
                ttlPolicies: [], TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(write.Success, write.Failure?.Message);

            var group = new ChangeSetJournal(_databaseCoreManager.DatabaseCore).Get(changeSetId);

            Assert.NotNull(group);
            Assert.Equal(2, group.Entries.Count);
            Assert.Contains(group.Entries, e => e.Kind == ChangeSetEntryKind.SensorDataPolicies);
            Assert.Contains(group.Entries, e => e.Kind == ChangeSetEntryKind.SensorTtlPolicies);
        }

        [Fact]
        public async Task TemplateWrite_LeavesAJournalRecord()
        {
            // Templates have no cache journal hook — the recorder IS their
            // journal writer (#1588).
            var changeSetId = Guid.NewGuid();

            var created = await new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                    _alertScheduleProvider, _authorization.Object, _users.Object,
                    new ChangeSetRecorder(new ChangeSetJournal(_databaseCoreManager.DatabaseCore), _journalService,
                        NullLogger<ChangeSetRecorder>.Instance),
                    NullLogger<AlertTemplateAdministrationService>.Instance)
                .CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnTemplate"]),
                    new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal(JournalKinds.AlertTemplate, record.Kind);
            Assert.Equal(changeSetId.ToString(), record.ChangeSetId);
            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task Rollback_ByRecord_UndoesTheGroup_AndLeavesARedoRecord()
        {
            var sensor = await CreateSensorAsync("jrnRollback");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);
            Assert.Single(sensor.Policies); // the write applied

            var record = RecordOf(changeSetId);

            var rolledBack = await _journal.RollbackAsync(record.Id, TokenUser);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);

            // The policy is gone and the rollback group left its own
            // journal record (the redo handle is visible in the journal).
            Assert.Empty(sensor.Policies);

            var redoId = Guid.Parse(rolledBack.Value.RollbackChangeSetId);
            var redoRecord = RecordOf(redoId);

            Assert.Equal(JournalKinds.SensorDataPolicies, redoRecord.Kind);
            Assert.True(redoRecord.Rollbackable);
        }

        [Fact]
        public async Task Rollback_ByRecord_NonAdminTokenOwner_IsForbidden()
        {
            // #1588 breaking change: Manager tokens can no longer roll back —
            // the token's owner must be an admin (the per-resource write
            // checks stay as the second line of defense inside the engine).
            var sensor = await CreateSensorAsync("jrnNonAdmin");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            _owner.IsAdmin = false;

            var refused = await _journal.RollbackAsync(RecordOf(changeSetId).Id, TokenUser);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Forbidden, refused.Failure.Outcome);
            Assert.Equal(JournalAdministrationService.JournalRollbackAccessDeniedMessage, refused.Failure.Message);
        }

        [Fact]
        public async Task Rollback_ByNonRollbackableRecord_IsConflict()
        {
            // Tree CRUD and node settings are journal-visible only — no undo
            // group, no rollback.
            var productId = Guid.NewGuid();

            _journalService.AddRecord(new JournalRecordModel(productId, InitiatorInfo.AsUser("tree-admin"))
            {
                Enviroment = "Create product",
                PropertyName = "product",
                Kind = JournalKinds.TreeCrud,
                ProductId = productId.ToString(),
            });

            var listed = _journal.ListRecords(TokenUser, new JournalFilter { ProductId = productId }).Value;
            var record = Assert.Single(listed);

            Assert.False(record.Rollbackable);

            var refused = await _journal.RollbackAsync(record.Id, TokenUser);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);
        }

        [Fact]
        public async Task ListRecords_FiltersByKindAuthorAndProduct()
        {
            var sensorA = await CreateSensorAsync("jrnFilterA");
            var sensorB = await CreateSensorAsync("jrnFilterB");
            var groupA = Guid.NewGuid();
            var groupB = Guid.NewGuid();

            var first = await _policies.CreateSensorPolicyAsync(sensorA.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = groupA });
            var second = await _policies.CreateSensorPolicyAsync(sensorB.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = groupB });
            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);

            var byKind = _journal.ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.SensorDataPolicies, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            Assert.Contains(byKind, r => r.ChangeSetId == groupA.ToString());
            Assert.Contains(byKind, r => r.ChangeSetId == groupB.ToString());
            Assert.All(byKind, r => Assert.Equal(JournalKinds.SensorDataPolicies, r.Kind));

            var byProduct = _journal.ListRecords(TokenUser,
                new JournalFilter { ProductId = _fixture.ProductAId, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            Assert.All(byProduct, r => Assert.Equal(_fixture.ProductAId.ToString(), r.ProductId));
        }

        [Fact]
        public async Task GetRecord_ReturnsTheUndoGroupDetails()
        {
            var sensor = await CreateSensorAsync("jrnDetails");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);
            var details = _journal.GetRecord(record.Id, TokenUser);

            Assert.True(details.Success, details.Failure?.Message);
            Assert.NotNull(details.Value.ChangeSet);
            var entry = Assert.Single(details.Value.ChangeSet.Entries);

            Assert.Equal("sensorDataPolicies", entry.Kind);
        }

        [Fact]
        public void RequestGroupId_IsMintedOncePerHttpContext()
        {
            // One HTTP request = one rollback group (#1588): the id is
            // minted on first use and cached in HttpContext.Items.
            var context = new DefaultHttpContext();

            var first = ChangeSetHeaders.RequestGroupId(context);
            var second = ChangeSetHeaders.RequestGroupId(context);

            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);

            var other = ChangeSetHeaders.RequestGroupId(new DefaultHttpContext());

            Assert.NotEqual(first, other);
        }


        private JournalRecordDto RecordOf(Guid changeSetId)
        {
            var listed = _journal.ListRecords(TokenUser,
                new JournalFilter { FromUtc = DateTime.UtcNow.AddMinutes(-5), Limit = 200 }).Value;

            return listed.Single(r => r.ChangeSetId == changeSetId.ToString());
        }

        private async Task<BaseSensorModel> CreateSensorAsync(string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            return sensor;
        }

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static PolicyDto DataPolicy() => new()
        {
            Icon = "policy",
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    Target = System.Text.Json.JsonSerializer.SerializeToElement(20),
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
            Policies = [],
        };
    }
}
