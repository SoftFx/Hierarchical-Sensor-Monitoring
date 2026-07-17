using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Controllers;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Controllers
{
    // Regression coverage for #1210: Alert Template save must block when a path template matches an
    // existing sensor whose type differs from the template's selected concrete type. Without this
    // check the template saves silently and AlertTemplateModel.IsMatch later skips those sensors.
    public class AlertTemplatesControllerTests
    {
        private readonly Mock<ITreeValuesCache> _cacheMock = new();
        private readonly Mock<IFolderManager> _folderManagerMock = new();
        private readonly Mock<IUserManager> _userManagerMock = new();
        private readonly Mock<IChatsManager> _chatsMock = new();
        private readonly Mock<IAlertScheduleProvider> _scheduleProviderMock = new();
        private readonly TreeViewModel _treeViewModel;
        private readonly Guid _folderId = Guid.NewGuid();


        public AlertTemplatesControllerTests()
        {
            _cacheMock.Setup(c => c.GetAlertTemplateModels()).Returns(new List<AlertTemplateModel>());
            _cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());
            _cacheMock.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult((true, (string)null)));

            _userManagerMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            _folderManagerMock.Setup(f => f.GetUserFolders(It.IsAny<User>())).Returns(new List<FolderModel>());
            _scheduleProviderMock.Setup(p => p.GetAllSchedules()).Returns(new List<AlertSchedule>());
            _chatsMock.Setup(s => s.GetValues()).Returns(new List<Chat>());

            _treeViewModel = new TreeViewModel(_cacheMock.Object, _folderManagerMock.Object, _userManagerMock.Object);
        }


        private AlertTemplatesController CreateController()
        {
            var controller = new AlertTemplatesController(
                _chatsMock.Object,
                _folderManagerMock.Object,
                _treeViewModel,
                _cacheMock.Object,
                _userManagerMock.Object,
                _scheduleProviderMock.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new User() },
            };

            return controller;
        }

        private static BaseSensorModel BuildSensor(SensorType type) =>
            TestSensorModelFactory.Build(new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                Type = (byte)type,
            });

        private DataAlertTemplateViewModel BuildData(byte type, params string[] paths) => new()
        {
            Id = Guid.NewGuid(),
            Name = $"Template-{Guid.NewGuid():N}",
            FolderId = _folderId,
            Type = type,
            PathTemplates = paths.ToList(),
        };


        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task MixedTypePaths_AddsPathTemplatesError()
        {
            // Template configured for Integer, but its path matches a Double sensor.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/mixedTypeSensor");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(DataAlertTemplateViewModel.PathTemplates)));

            var message = controller.ModelState[nameof(DataAlertTemplateViewModel.PathTemplates)].Errors[0].ErrorMessage;
            Assert.Contains("Double", message);
            Assert.Contains("Integer", message);
            Assert.Contains("separate Alert Template", message);
        }

        // Regression coverage for #1247: when validation blocks Save, the AlertTemplate POST
        // returns _AlertTemplate and the client re-renders the form via replaceWith. That client
        // fix only works if the server echoes user-entered values back in the rebuilt viewmodel.
        // Id matters for the Edit path — a different Id after re-render would make the second Save
        // create a new template instead of updating the one being edited. If a future refactor
        // rebuilds from a different source (cached template, default viewmodel), this test fails
        // instead of silently breaking the second Save.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task ValidationFailure_PreservesUserEnteredData()
        {
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/intPath", "*/mixedPath");
            data.Name = "UserEnteredName";
            var expectedId = data.Id;

            var result = await controller.AlertTemplate(data);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("_AlertTemplate", partial.ViewName);
            var model = Assert.IsType<DataAlertTemplateViewModel>(partial.Model);
            Assert.Equal(expectedId, model.Id);
            Assert.Equal("UserEnteredName", model.Name);
            Assert.Equal((byte)SensorType.Integer, model.Type);
            Assert.Equal(_folderId, model.FolderId);
            Assert.Contains("*/intPath", model.PathTemplates);
            Assert.Contains("*/mixedPath", model.PathTemplates);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task AllCompatiblePaths_ModelStateValid()
        {
            // Two paths, both match Integer sensors (same as template type) — save must proceed.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Integer) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/intSensorA", "*/intSensorB");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
            // Verify the mismatch check actually ran for each path — without this assertion the test
            // would still pass if the validation was silently removed, because AddAlertTemplateAsync
            // is mocked to succeed unconditionally.
            _cacheMock.Verify(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()), Times.Exactly(2));
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task PathMatchingNoSensors_ModelStateValid()
        {
            // Wildcard path with no current matches (future sensor) must be allowed.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel>());

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/futureSensor");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task AnyTypeTemplate_SkipsMismatchCheck()
        {
            // AnyType templates match every sensor type by design — mismatch check must not fire
            // even when matching sensors exist with a different concrete type.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData(DataAlertTemplateViewModel.AnyType, "*/anySensor");

            var result = await controller.AlertTemplate(data);

            Assert.True(controller.ModelState.IsValid);
            Assert.IsType<OkResult>(result);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task MismatchedPath_BlocksSave_EvenWithoutRegularAlerts()
        {
            // The mismatch check must fire for any template with a concrete type, regardless of
            // whether regular (non-TTL) policies are configured. AlertTemplateModel.IsMatch filters
            // by type uniformly across alert kinds, so a TTL-only or freshly-opened template would
            // silently skip mismatched-type sensors just like a regular-alert template.
            // Regression guard: if a future refactor narrows the check to templates that have
            // regular policies, this test must fail.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            // No DataAlerts configured (no regular policies, no TTL entries) — mimics a freshly
            // opened editor where the user has only picked sensor type + paths so far.
            var data = BuildData((byte)SensorType.Integer, "*/noRegularAlertsMismatch");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            Assert.True(controller.ModelState.ContainsKey(nameof(DataAlertTemplateViewModel.PathTemplates)));
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task MultipleMismatchedPaths_EmitsErrorPerPath()
        {
            // Each offending path produces its own field error; the user should see all conflicts,
            // not just the first one.
            _cacheMock.Setup(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()))
                .Returns(new List<BaseSensorModel> { BuildSensor(SensorType.Double) });

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/badPathOne", "*/badPathTwo", "*/badPathThree");

            await controller.AlertTemplate(data);

            Assert.False(controller.ModelState.IsValid);
            var errors = controller.ModelState[nameof(DataAlertTemplateViewModel.PathTemplates)].Errors;
            Assert.Equal(3, errors.Count);
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathOne"));
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathTwo"));
            Assert.Contains(errors, e => e.ErrorMessage.Contains("badPathThree"));
            // Verifies the helper iterates paths rather than short-circuiting on the first mismatch.
            _cacheMock.Verify(c => c.GetSensors(It.IsAny<string>(), It.IsAny<SensorType?>(), It.IsAny<Guid?>()), Times.Exactly(3));
        }

        // Regression coverage for #1246: when a demoted TTL row's Operation is missing from the
        // form data (the client-side race where collectAlerts runs before the GetOperation AJAX
        // completes), the server silently drops the condition — Property=Value is bound but
        // Operation=null, so ToUpdate's `if (condition.Operation != null)` guard skips it.
        // The resulting policy has zero conditions. This test documents that behavior so a future
        // server-side change can't accidentally start auto-filling Operation; the real fix lives
        // in submitForm (waits for the AJAX before collecting).
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public async Task AlertTemplate_DemotedRowWithoutOperation_SavesPolicyWithNoConditions()
        {
            AlertTemplateModel captured = null;
            _cacheMock.Setup(c => c.AddAlertTemplateAsync(It.IsAny<AlertTemplateModel>(), It.IsAny<CancellationToken>()))
                .Callback<AlertTemplateModel, CancellationToken>((m, _) => captured = m)
                .Returns(Task.FromResult((true, (string)null)));

            var controller = CreateController();
            var data = BuildData((byte)SensorType.Integer, "*/intSensor");
            data.DataAlerts = new Dictionary<byte, List<DataAlertViewModelBase>>
            {
                [(byte)SensorType.Integer] =
                [
                    new DataAlertViewModelBase
                    {
                        Id = Guid.NewGuid(),
                        Conditions = { new ConditionViewModel { Property = AlertProperty.Value } },
                    },
                ],
            };

            var result = await controller.AlertTemplate(data);

            Assert.IsType<OkResult>(result);
            Assert.NotNull(captured);
            Assert.Single(captured.Policies);
            Assert.Empty(captured.Policies[0].Conditions);
        }
        // Regression coverage for #1244: UpdateTemplate feeds the live-refreshed notification dropdown.
        // Post-#1262 the payload is one unified Chats array (no more Groups/Users/SlackDestinations split):
        // each Chat appears exactly once regardless of how many channels it carries.
        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void UpdateTemplate_ReturnsUnifiedChatsList()
        {
            var folderId = Guid.NewGuid();
            var telegramChat = BuildTelegramChat(Guid.NewGuid(), "tg-group", ConnectedChatType.TelegramGroup);
            var slackChat = BuildSlackChat(Guid.NewGuid(), "slack-channel");

            var folder = new FolderModel(BuildFolderEntity(folderId, telegramChat.Id, slackChat.Id));

            _folderManagerMock.Setup(f => f.TryGetValue(folderId, out folder)).Returns(true);
            _chatsMock.Setup(t => t.GetValues()).Returns(new List<Chat> { telegramChat, slackChat });

            var controller = CreateController();

            var result = controller.UpdateTemplate(DataAlertTemplateViewModel.AnyType, "[]", folderId);

            var json = Assert.IsType<JsonResult>(result);
            using var doc = JsonDocument.Parse((string)json.Value);
            var chats = doc.RootElement.GetProperty("Chats").GetProperty("Chats").EnumerateArray().ToList();

            Assert.Equal(2, chats.Count);
            var byId = chats.ToDictionary(c => Guid.Parse(c.GetProperty("Id").GetString()));

            Assert.Contains(telegramChat.Id, byId.Keys);
            Assert.Equal(telegramChat.Name, byId[telegramChat.Id].GetProperty("Name").GetString());
            Assert.False(string.IsNullOrEmpty(byId[telegramChat.Id].GetProperty("IconsHtml").GetString()),
                "Telegram chat should carry brand icon HTML");

            Assert.Contains(slackChat.Id, byId.Keys);
            Assert.Equal(slackChat.Name, byId[slackChat.Id].GetProperty("Name").GetString());
            Assert.False(string.IsNullOrEmpty(byId[slackChat.Id].GetProperty("IconsHtml").GetString()),
                "Slack chat should carry brand icon HTML");
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void UpdateTemplate_FiltersChatsByFolderBinding()
        {
            // A chat bound to a different folder must not appear in this folder's dropdown.
            // Mirrors the GetAvailableChats availability rule (bound → only its folder; unbound → global).
            var folderId = Guid.NewGuid();
            var boundChat = BuildSlackChat(Guid.NewGuid(), "bound-slack");
            boundChat.Folders.Add(folderId);

            var unboundChat = BuildSlackChat(Guid.NewGuid(), "other-folder-slack");
            unboundChat.Folders.Add(Guid.NewGuid()); // tied to a different folder

            var folder = new FolderModel(BuildFolderEntity(folderId, boundChat.Id));

            _folderManagerMock.Setup(f => f.TryGetValue(folderId, out folder)).Returns(true);
            _chatsMock.Setup(t => t.GetValues()).Returns(new List<Chat> { boundChat, unboundChat });

            var controller = CreateController();

            var result = controller.UpdateTemplate(DataAlertTemplateViewModel.AnyType, "[]", folderId);

            var json = Assert.IsType<JsonResult>(result);
            using var doc = JsonDocument.Parse((string)json.Value);
            var chatIds = doc.RootElement
                .GetProperty("Chats")
                .GetProperty("Chats")
                .EnumerateArray()
                .Select(c => Guid.Parse(c.GetProperty("Id").GetString()))
                .ToList();

            Assert.Single(chatIds);
            Assert.Equal(boundChat.Id, chatIds[0]);
        }

        [Fact]
        [Trait("Category", "Alert Template authoring")]
        public void UpdateTemplate_ReturnsNullChats_WhenFolderNotFound()
        {
            // When the folder lookup misses, Chats stays null so the JS `if (data.Chats != null)`
            // guard skips the dropdown rebuild entirely. A non-null empty payload would wipe the
            // existing selection — the null contract is load-bearing.
            var unknownFolderId = Guid.NewGuid();
            FolderModel folder = null;
            _folderManagerMock.Setup(f => f.TryGetValue(unknownFolderId, out folder)).Returns(false);

            var controller = CreateController();

            var result = controller.UpdateTemplate(DataAlertTemplateViewModel.AnyType, "[]", unknownFolderId);

            var json = Assert.IsType<JsonResult>(result);
            using var doc = JsonDocument.Parse((string)json.Value);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("Chats").ValueKind);
        }


        private static Chat BuildTelegramChat(Guid id, string name, ConnectedChatType type) =>
            new(new ChatEntity
            {
                Id = id.ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = name,
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = long.MaxValue,
                TelegramType = (byte)type,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

        private static Chat BuildSlackChat(Guid id, string name) =>
            new(new ChatEntity
            {
                Id = id.ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = name,
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                SlackWebhookUrl = "https://hooks.slack.com/services/test",
            });

        private static FolderEntity BuildFolderEntity(Guid folderId, params Guid[] chatIds) =>
            new()
            {
                Id = folderId.ToString(),
                DisplayName = "Test folder",
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                Chats = chatIds.Select(c => c.ToByteArray()).ToList(),
            };
    }
}
