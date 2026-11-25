using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMCommon.Model;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1829: the chat write surface of the management API — the token-side twin of the web
    // UI's chat editor: create (admin only), update and delete (admin or the manager of a
    // bound folder), on the same write engine the web flows run.
    public sealed class ChatWriteServiceTests
    {
        private const string SlackUrl = ChatWriteHarness.SlackUrl;
        private const string MattermostUrl = ChatWriteHarness.MattermostUrl;
        private const string FolderNotManagedMessage = ChatWriteHarness.FolderNotManagedMessage;

        private static readonly Guid OwnerId = ChatWriteHarness.OwnerId;

        // The service writes into an in-memory world (ChatWriteHarness), so every echo
        // reads back what the engine stored.
        private readonly ChatWriteHarness _world = new();

        private ChatWriteService Service => _world.CreateWriter();

        private ClaimsPrincipal Token => _world.Token;

        private FolderModel AddFolder(params Guid[] boundChats) => _world.AddFolder(boundChats);

        private Chat AddChat(string name, bool global = true) => _world.AddChat(name, global);


        [Fact]
        public async Task CreateChat_Admin_StoresTheChat_AndEchoesWhatWasStored()
        {
            var result = await Service.CreateChat(new CreateChatRequestDto
            {
                Name = "Ops alerts",
                Description = "on-call",
                SlackWebhookUrl = SlackUrl,
            }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            var chat = Assert.Single(_world.Store);
            Assert.Equal(result.Value.Id, chat.Id);
            Assert.Equal(OwnerId, chat.AuthorId);
            Assert.Equal("Ops alerts", result.Value.Name);
            Assert.Equal("on-call", result.Value.Description);
            Assert.True(result.Value.HasSlackWebhook);
            Assert.False(result.Value.HasMattermostWebhook);
            Assert.Empty(result.Value.Folders);
        }

        [Fact]
        public async Task CreateChat_NonAdmin_IsForbidden_AndStoresNothing()
        {
            // The web UI's AddChat is admin-only — a chat created with an attacker-supplied
            // webhook would exfiltrate every alert sent to it.
            _world.Authorization.Setup(a => a.AuthorizeChatCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Forbidden);

            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts" }, Token);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Empty(_world.Store);
        }

        // ---- create: what a request may ask for -----------------------------------


        [Fact]
        public async Task CreateChat_NameHeldByAnotherChat_IsRejectedUnderName_AndStoresNothing()
        {
            AddChat("Ops alerts");

            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "OPS ALERTS" }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Chat with the same name already exists."], result.Failure.Errors["name"]);
            Assert.Single(_world.Store);
        }

        // #1705, on the API/MCP paths only: a chat name reaches the model context through
        // list_world.Chats, and no legitimate name carries control or bidi-override characters.
        [Fact]
        public async Task CreateChat_NameWithABidiOverride_IsRejectedUnderName_AndStoresNothing()
        {
            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops\u202Ealerts" }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains("control or bidirectional-override", Assert.Single(result.Failure.Errors["name"]));
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task CreateChat_FolderTheOwnerDoesNotManage_IsRejectedUnderFolders_AndStoresNothing()
        {
            var folder = AddFolder();
            _world.Unmanaged.Add(folder.Id);

            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", Folders = [folder.Id] }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([FolderNotManagedMessage], result.Failure.Errors["folders"]);
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task CreateChat_UnknownFolder_AnswersTheSameError_AsAnUnmanagedOne()
        {
            // An unknown folder id and a folder the owner cannot manage are indistinguishable
            // in the answer: the message discloses nothing about folders outside their reach.
            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", Folders = [Guid.NewGuid()] }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([FolderNotManagedMessage], result.Failure.Errors["folders"]);
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task CreateChat_WithFolders_BindsTheChatToThem_AndEchoesTheBindings()
        {
            var folderA = AddFolder();
            var folderB = AddFolder();

            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", Folders = [folderA.Id, folderB.Id] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal(
                new[] { folderA.Id, folderB.Id }.OrderBy(id => id),
                result.Value.Folders.OrderBy(id => id));
            Assert.Contains(result.Value.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.Contains(result.Value.Id, _world.FolderStore.Single(f => f.Id == folderB.Id).Chats);
        }

        [Fact]
        public async Task CreateChat_WebhookThatIsNotAnAbsoluteHttpUrl_IsRejectedUnderItsField()
        {
            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", SlackWebhookUrl = "hooks slack" }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Webhook URL must be a valid URL"], result.Failure.Errors["slackWebhookUrl"]);
            Assert.Empty(_world.Store);
        }

        [Fact]
        public async Task CreateChat_SeveralProblems_AreAnsweredInOneBody()
        {
            // A caller with several mistakes learns every field at once (the area's rule).
            var folder = AddFolder();
            _world.Unmanaged.Add(folder.Id);
            AddChat("Ops alerts");

            var result = await Service.CreateChat(new CreateChatRequestDto
            {
                Name = "OPS ALERTS",
                MattermostWebhookUrl = "not a url",
                Folders = [folder.Id],
            }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(
                new[] { "folders", "mattermostWebhookUrl", "name" },
                result.Failure.Errors.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.Single(_world.Store);
        }

        // ---- update: a partial write on a chat the caller may change ----------------


        [Fact]
        public async Task UpdateChat_ChangesOnlyTheGivenFields_AndEchoesTheStoredChat()
        {
            var chat = AddChat("Ops alerts");
            chat.Update(new ChatUpdate { Id = chat.Id, Description = "on-call", MattermostWebhookUrl = MattermostUrl });

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto
            {
                Name = "On-call",
                SendMessages = false,
                MessagesAggregationTimeSec = 300,
                SlackWebhookUrl = SlackUrl,
            }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("On-call", result.Value.Name);
            Assert.Equal("on-call", result.Value.Description);                 // omitted: unchanged
            Assert.False(result.Value.SendMessages);
            Assert.Equal(300, result.Value.MessagesAggregationTimeSec);
            Assert.True(result.Value.HasSlackWebhook);
            Assert.True(result.Value.HasMattermostWebhook);                     // omitted: unchanged
        }

        [Fact]
        public async Task UpdateChat_EmptyWebhookValue_ChangesNothing()
        {
            // A read never returns the URL, so there is nothing to echo back: an empty
            // value means "no change" exactly like an omitted field.
            var chat = AddChat("Ops alerts");
            chat.Update(new ChatUpdate { Id = chat.Id, SlackWebhookUrl = SlackUrl });

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { SlackWebhookUrl = "  " }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal(SlackUrl, _world.Store.Single().SlackWebhookUrl);
            Assert.True(result.Value.HasSlackWebhook);
        }

        [Fact]
        public async Task UpdateChat_ClearFlags_RemoveTheChannels_AndTheEchoSaysSo()
        {
            var chat = AddChat("Ops alerts");
            chat.TelegramType = ConnectedChatType.TelegramGroup;
            chat.Update(new ChatUpdate
            {
                Id = chat.Id,
                TelegramChatId = 555_000_111L,
                SlackWebhookUrl = SlackUrl,
                MattermostWebhookUrl = MattermostUrl,
            });

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto
            {
                ClearSlackWebhook = true,
                ClearMattermostWebhook = true,
                ClearTelegramBinding = true,
            }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.False(result.Value.HasSlackWebhook);
            Assert.False(result.Value.HasMattermostWebhook);
            Assert.Null(result.Value.TelegramType);
            Assert.Null(_world.Store.Single().SlackWebhookUrl);
            Assert.Null(_world.Store.Single().MattermostWebhookUrl);
            Assert.Null(_world.Store.Single().TelegramChatId);
        }

        [Fact]
        public async Task UpdateChat_ClearFlagSetToFalse_RemovesNothing()
        {
            var chat = AddChat("Ops alerts");
            chat.Update(new ChatUpdate { Id = chat.Id, SlackWebhookUrl = SlackUrl });

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { ClearSlackWebhook = false }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal(SlackUrl, _world.Store.Single().SlackWebhookUrl);
        }

        [Fact]
        public async Task UpdateChat_NameHeldByAnotherChat_IsRejectedUnderName_AndChangesNothing()
        {
            var chat = AddChat("Ops alerts");
            AddChat("On-call");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "ON-CALL" }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Chat with the same name already exists."], result.Failure.Errors["name"]);
            Assert.Equal("Ops alerts", _world.Store.Single(c => c.Id == chat.Id).Name);
        }

        [Fact]
        public async Task UpdateChat_ItsOwnNameInAnotherLetterCase_IsAccepted()
        {
            var chat = AddChat("Ops alerts");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "OPS ALERTS" }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("OPS ALERTS", result.Value.Name);
        }

        [Fact]
        public async Task UpdateChat_NameWithABidiOverride_IsRejectedUnderName_AndChangesNothing()
        {
            var chat = AddChat("Ops alerts");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops\u202Ealerts" }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Single(result.Failure.Errors["name"]);
            Assert.Equal("Ops alerts", _world.Store.Single(c => c.Id == chat.Id).Name);
        }


        // ---- update: admission ------------------------------------------------------


        [Fact]
        public async Task UpdateChat_UnknownChat_IsNotFound()
        {
            var result = await Service.UpdateChat(Guid.NewGuid(), new UpdateChatRequestDto { Name = "Ops alerts" }, Token);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task UpdateChat_ChatBoundOnlyToAnInvisibleFolder_IsNotFound_LikeAnUnknownOne()
        {
            // The area's anti-enumeration rule: a chat outside the caller's sight and an
            // unknown id answer the same 404.
            var chat = AddChat("Ops alerts", global: false);
            _world.Authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops alerts 2" }, Token);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            Assert.Equal("Ops alerts", _world.Store.Single(c => c.Id == chat.Id).Name);
        }

        [Fact]
        public async Task UpdateChat_VisibleChatTheCallerCannotChange_IsForbidden()
        {
            // The caller sees the folder the chat is bound to but does not manage it.
            var chat = AddChat("Ops alerts", global: false);
            _world.Authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops alerts 2" }, Token);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Equal(PolicyWriteResult.WriteDeniedMessage, result.Failure.Message);
            Assert.Equal("Ops alerts", _world.Store.Single(c => c.Id == chat.Id).Name);
        }

        [Fact]
        public async Task UpdateChat_AdmissionPrecedesValidation()
        {
            // A body-shape error must not reveal that a chat exists outside the caller's reach.
            _world.Authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(false);
            var chat = AddChat("Ops alerts", global: false);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = new string('x', 101) }, Token);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
        }


        // ---- update: the folder bindings -------------------------------------------


        [Fact]
        public async Task UpdateChat_Folders_ReplacesTheBindingsWithinTheFoldersTheOwnerManages()
        {
            // A caller listing only their own folder never unbinds the others: A (managed,
            // not listed) goes, B (not managed) stays, C (managed, listed) comes.
            var chat = AddChat("Ops alerts");
            var folderA = AddFolder(chat.Id);
            var folderB = AddFolder(chat.Id);
            var folderC = AddFolder();
            _world.Unmanaged.Add(folderB.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [folderC.Id] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderB.Id).Chats);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderC.Id).Chats);
            // The echo lists every binding the caller can see — B included: it is visible,
            // only not theirs to manage.
            Assert.Equal(
                new[] { folderB.Id, folderC.Id }.OrderBy(id => id),
                result.Value.Folders.OrderBy(id => id));
        }

        [Fact]
        public async Task UpdateChat_WithoutFolders_LeavesTheBindingsAlone()
        {
            var chat = AddChat("Ops alerts");
            var folderA = AddFolder(chat.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Description = "on-call" }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.Equal([folderA.Id], result.Value.Folders);
        }

        [Fact]
        public async Task UpdateChat_EmptyFolders_MakesTheChatGlobal()
        {
            var chat = AddChat("Ops alerts");
            var folderA = AddFolder(chat.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.Empty(result.Value.Folders);
        }

        [Fact]
        public async Task UpdateChat_FolderTheOwnerDoesNotManage_IsRejectedUnderFolders_AndChangesNothing()
        {
            var chat = AddChat("Ops alerts");
            var folderA = AddFolder(chat.Id);
            var folderB = AddFolder();
            _world.Unmanaged.Add(folderB.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [folderA.Id, folderB.Id] }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([FolderNotManagedMessage], result.Failure.Errors["folders"]);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == folderB.Id).Chats);
        }


        // ---- delete -----------------------------------------------------------------


        [Fact]
        public async Task DeleteChat_DryRun_AnswersTheUsageCount_AndRemovesNothing()
        {
            // The counter behind the web list's usage badge: how many sensors' effective
            // alert configuration references the chat.
            var chat = AddChat("Ops alerts");
            var folder = AddFolder(chat.Id);
            _world.Usage.Setup(u => u.Compute()).Returns((new Dictionary<Guid, int> { [chat.Id] = 7 }, 0));

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: true);

            Assert.True(result.Success, result.Failure?.Message);
            var preview = Assert.IsType<ChatDeletePreviewDto>(result.Preview);
            Assert.True(preview.DryRun);
            Assert.Equal("chat", preview.Kind);
            Assert.Equal("delete", preview.Operation);
            Assert.Equal(chat.Id.ToString(), preview.EntityId);
            Assert.Equal("Ops alerts", preview.Path);
            Assert.Equal(7, preview.SensorUsageCount);
            Assert.False(preview.SensorUsageIncomplete);
            Assert.Contains(preview.Affected, entity => entity.Id == chat.Id.ToString() && entity.Change == "remove");
            Assert.Contains(preview.Affected, entity => entity.Id == folder.Id.ToString() && entity.Change == "unbind");
            Assert.Single(_world.Store);
            Assert.Contains(chat.Id, _world.FolderStore.Single().Chats);
        }

        [Fact]
        public async Task DeleteChat_DryRun_CountIsALowerBound_WhenSensorsWereSkipped()
        {
            // The counter skips a sensor it cannot read mid-mutation; the preview says so
            // instead of passing a partial count for the whole.
            var chat = AddChat("Ops alerts");
            _world.Usage.Setup(u => u.Compute()).Returns((new Dictionary<Guid, int> { [chat.Id] = 4 }, 2));

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: true);

            var preview = Assert.IsType<ChatDeletePreviewDto>(result.Preview);
            Assert.Equal(4, preview.SensorUsageCount);
            Assert.True(preview.SensorUsageIncomplete);
        }

        [Fact]
        public async Task DeleteChat_Applied_RemovesTheChat_NamingTheTokenOwnerAsInitiator()
        {
            var chat = AddChat("Ops alerts");

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.False(result.IsDryRun);
            Assert.Empty(_world.Store);
            _world.Chats.Verify(c => c.TryRemove(It.Is<RemoveRequest>(r =>
                r.Id == chat.Id
                && r.Initiator.Type == InitiatorType.User
                && r.Initiator.Info == "admin")), Times.Once);
        }

        [Fact]
        public async Task DeleteChat_Applied_PrunesTheFolderBindings()
        {
            var chat = AddChat("Ops alerts");
            var folder = AddFolder(chat.Id);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == folder.Id).Chats);
        }

        [Fact]
        public async Task DeleteChat_RemovalThatNeverReachedTheStore_IsConflict_NeverAGhostSuccess()
        {
            var chat = AddChat("Ops alerts");
            _world.Chats.Setup(c => c.TryRemove(It.IsAny<RemoveRequest>())).ReturnsAsync(false);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            Assert.Single(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_ChatThatVanishedOnTheWay_IsNotFound()
        {
            var chat = AddChat("Ops alerts");
            _world.Chats.Setup(c => c.TryRemove(It.IsAny<RemoveRequest>())).ReturnsAsync(false)
                .Callback<RemoveRequest>(remove => _world.Store.RemoveAll(c => c.Id == remove.Id));

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }


        // ---- delete: admission ------------------------------------------------------


        [Fact]
        public async Task DeleteChat_UnknownChat_IsNotFound()
        {
            var result = await Service.DeleteChat(Guid.NewGuid(), Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task DeleteChat_ChatOutsideTheCallersSight_IsNotFound_LikeAnUnknownOne()
        {
            var chat = AddChat("Ops alerts", global: false);
            _world.Authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(false);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            Assert.Single(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_VisibleChatTheCallerCannotChange_IsForbidden_AndRemovesNothing()
        {
            var chat = AddChat("Ops alerts", global: false);
            _world.Authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(false);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
            Assert.Single(_world.Store);
        }
    }
}
