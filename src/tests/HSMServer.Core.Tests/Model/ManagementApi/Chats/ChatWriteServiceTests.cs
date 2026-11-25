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
        private const string MakingGlobalDeniedMessage = ChatWriteHarness.MakingGlobalDeniedMessage;

        private static readonly Guid OwnerId = ChatWriteHarness.OwnerId;

        // The service writes into an in-memory world (ChatWriteHarness), so every echo
        // reads back what the engine stored.
        private readonly ChatWriteHarness _world = new();

        private ChatWriteService Service => _world.CreateWriter();

        private ClaimsPrincipal Token => _world.Token;

        private FolderModel AddFolder(params Guid[] boundChats) => _world.AddFolder(boundChats);

        private Chat AddChat(string name, bool global = true, string description = null) => _world.AddChat(name, global, description);


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

        // A negative aggregation delay never flushes a channel's accumulator: stored, the
        // chat would drop its alerts in silence. The engine's range rule reaches the wire
        // under the DTO's own name.
        [Theory]
        [InlineData(-1)]
        [InlineData(86_401)]
        public async Task UpdateChat_DelayOutsideTheRange_IsRejectedUnderItsField_AndChangesNothing(int delay)
        {
            var chat = AddChat("Ops alerts");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { MessagesAggregationTimeSec = delay }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Messages delay must be between 0 and 86400 seconds."], result.Failure.Errors["messagesAggregationTimeSec"]);
            Assert.Equal(60, _world.Store.Single().MessagesAggregationTimeSec);
        }

        [Fact]
        public async Task CreateChat_NegativeDelay_IsRejectedUnderItsField_AndStoresNothing()
        {
            var result = await Service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", MessagesAggregationTimeSec = -1 }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Messages delay must be between 0 and 86400 seconds."], result.Failure.Errors["messagesAggregationTimeSec"]);
            Assert.Empty(_world.Store);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(86_400)]
        public async Task UpdateChat_DelayAtTheEdgesOfTheRange_IsAccepted_AndEchoed(int delay)
        {
            var chat = AddChat("Ops alerts");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { MessagesAggregationTimeSec = delay }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal(delay, result.Value.MessagesAggregationTimeSec);
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
        public async Task UpdateChat_OfAChatDuplicatedBeforeTheRule_KeepingItsName_IsAccepted()
        {
            // Two chats named alike exist from before names were unique (the form always
            // posts the name, even unchanged): the rule guards changes, not history — the
            // chat's other settings stay editable while it keeps its name.
            var chat = AddChat("Ops alerts");
            AddChat("OPS ALERTS");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops alerts", Description = "on-call" }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("on-call", result.Value.Description);
        }

        // A row stored with a padded name, from before the name was held trimmed, reads
        // trimmed. Sending that name back unchanged is accepted — though another chat holds
        // it trimmed — and the row is not renamed behind the caller.
        [Fact]
        public async Task UpdateChat_OfAChatStoredWithAPaddedName_KeepingIt_IsAccepted_AndNotRenamed()
        {
            var chat = AddChat("Dev team ");
            AddChat("dev team");

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Dev team", Description = "on-call" }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("on-call", result.Value.Description);
            Assert.Equal("Dev team ", _world.Store.Single(c => c.Id == chat.Id).Name);
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
        public async Task UpdateChat_UnknownChat_IsNotFound_WithoutAskingTheEvaluator()
        {
            // An unknown id is the plain 404 with no evaluator call — nothing is disclosed.
            var result = await Service.UpdateChat(Guid.NewGuid(), new UpdateChatRequestDto { Name = "Ops alerts" }, Token);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            _world.Authorization.Verify(a => a.AuthorizeChatWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_AdmissionAsksTheEvaluatorForTheChatAndItsBindings()
        {
            // The sight and the change gate are the evaluator's (AuthorizeChatWrite, its
            // matrix lives in ApiTokenAuthorizationServiceTests): the service hands it the
            // chat and a snapshot of its bindings, before anything of the request is read.
            var chat = AddChat("Ops alerts");
            var folder = AddFolder(chat.Id);

            await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Description = "on-call" }, Token);

            _world.Authorization.Verify(a => a.AuthorizeChatWrite(Token, chat.Id,
                It.Is<IReadOnlyCollection<Guid>>(folders => folders.SequenceEqual(new[] { folder.Id }))), Times.Once);
        }

        [Theory]
        [InlineData(ApiTokenAuthorization.NotFound, PolicyWriteOutcome.NotFound)]
        [InlineData(ApiTokenAuthorization.Forbidden, PolicyWriteOutcome.Forbidden)]
        public async Task UpdateChat_RefusedByTheEvaluator_AnswersTheRefusal_AndChangesNothing(
            ApiTokenAuthorization decision, PolicyWriteOutcome outcome)
        {
            // NotFound: the chat is outside the caller's sight — the same 404 as an unknown
            // id. Forbidden: the caller sees the chat but may not change it.
            var chat = AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(decision);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops alerts 2" }, Token);

            Assert.Equal(outcome, result.Failure.Outcome);
            Assert.Equal("Ops alerts", _world.Store.Single(c => c.Id == chat.Id).Name);
        }

        [Fact]
        public async Task UpdateChat_AdmissionPrecedesValidation()
        {
            // A body-shape error must not reveal that a chat exists beyond the caller's reach.
            _world.RefuseChatWrites(ApiTokenAuthorization.Forbidden);
            var chat = AddChat("Ops alerts", global: false);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = new string('x', 101) }, Token);

            Assert.Equal(PolicyWriteOutcome.Forbidden, result.Failure.Outcome);
        }

        [Fact]
        public async Task UpdateChat_ThatLeavesTheChatOutsideTheCallersSight_EchoesNoBindings_AndNoDetail()
        {
            // The caller unbinds their own folder; the chat stays bound to a folder they
            // cannot see. The echo cannot name that folder, so its `folders` is empty — and
            // the chat is out of the caller's sight from here on (a later read is a 404).
            // The echo is judged like that later read: no binding they cannot see, and no
            // detail either — they manage none of the chat's folders any more. Both of the
            // evaluator's decisions follow the bindings they are given, as the real ones do,
            // so an echo judged on the chat BEFORE the write would still name the caller's
            // own folder and carry the detail.
            var chat = AddChat("Ops alerts", description: "on-call");
            var ownFolder = AddFolder(chat.Id);
            var unseenFolder = AddFolder(chat.Id);
            _world.Unmanaged.Add(unseenFolder.Id);
            _world.Authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns((ClaimsPrincipal _, ApiTokenResource resource) => resource.Id != unseenFolder.Id);
            _world.Authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns((ClaimsPrincipal _, IReadOnlyCollection<Guid> folders) => folders.Contains(ownFolder.Id));

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Empty(result.Value.Folders);
            Assert.Null(result.Value.Description);
            Assert.Null(result.Value.SendMessages);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == unseenFolder.Id).Chats);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == ownFolder.Id).Chats);
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

        // A read lists every bound folder the caller can SEE, the update accepts the ones
        // they manage: a client doing read-modify-write sends the read list back unchanged
        // and must not be refused for a binding that simply stays.
        [Fact]
        public async Task UpdateChat_ListingABoundFolderTheCallerDoesNotManage_KeepsIt_AndIsAccepted()
        {
            var chat = AddChat("Ops alerts");
            var folderA = AddFolder(chat.Id);
            var folderB = AddFolder(chat.Id);
            _world.Unmanaged.Add(folderB.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Name = "Ops", Folders = [folderA.Id, folderB.Id] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("Ops", result.Value.Name);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderA.Id).Chats);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folderB.Id).Chats);
        }

        [Fact]
        public async Task UpdateChat_BoundUnmanagedFolderBesideANewUnmanagedOne_IsRejectedUnderFolders()
        {
            // Listing a binding that stays is fine; the same list may not smuggle in a
            // folder the owner cannot manage.
            var chat = AddChat("Ops alerts");
            var folderB = AddFolder(chat.Id);
            var folderC = AddFolder();
            _world.Unmanaged.Add(folderB.Id);
            _world.Unmanaged.Add(folderC.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [folderB.Id, folderC.Id] }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([FolderNotManagedMessage], result.Failure.Errors["folders"]);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == folderC.Id).Chats);
        }

        // Making a chat global is an administrator's decision: unbound, a chat is offered
        // from every node and only an admin can change it afterwards.
        [Fact]
        public async Task UpdateChat_ByANonAdministrator_EmptyFolders_IsRejected_AndTheChatStaysBound()
        {
            _world.Owner.IsAdmin = false;
            var chat = AddChat("Ops alerts");
            var folder = AddFolder(chat.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [] }, Token);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([MakingGlobalDeniedMessage], result.Failure.Errors["folders"]);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == folder.Id).Chats);
        }

        [Fact]
        public async Task UpdateChat_ByANonAdministrator_UnbindingTheirFolder_WhileAnotherBindingStays_IsAccepted()
        {
            // The chat stays bound (to a folder they cannot manage), so it does not become
            // global — only outside their sight from here on.
            _world.Owner.IsAdmin = false;
            var chat = AddChat("Ops alerts");
            var ownFolder = AddFolder(chat.Id);
            var otherFolder = AddFolder(chat.Id);
            _world.Unmanaged.Add(otherFolder.Id);

            var result = await Service.UpdateChat(chat.Id, new UpdateChatRequestDto { Folders = [] }, Token);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.DoesNotContain(chat.Id, _world.FolderStore.Single(f => f.Id == ownFolder.Id).Chats);
            Assert.Contains(chat.Id, _world.FolderStore.Single(f => f.Id == otherFolder.Id).Chats);
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

        // The applied delete goes through IChatsManager.TryRemove — the entry whose
        // removal event prunes the chat from its folders and their policies; that the web
        // list's remove runs the same entry is what makes the pruning the same (it is the
        // manager's own behavior, pinned on the manager's side).

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
        public async Task DeleteChat_UnknownChat_IsNotFound_WithoutAskingTheEvaluator()
        {
            var result = await Service.DeleteChat(Guid.NewGuid(), Token, dryRun: false);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            _world.Authorization.Verify(a => a.AuthorizeChatWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);
        }

        [Theory]
        [InlineData(ApiTokenAuthorization.NotFound, PolicyWriteOutcome.NotFound)]
        [InlineData(ApiTokenAuthorization.Forbidden, PolicyWriteOutcome.Forbidden)]
        public async Task DeleteChat_RefusedByTheEvaluator_AnswersTheRefusal_AndRemovesNothing(
            ApiTokenAuthorization decision, PolicyWriteOutcome outcome)
        {
            var chat = AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(decision);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: false);

            Assert.Equal(outcome, result.Failure.Outcome);
            Assert.Single(_world.Store);
        }

        [Fact]
        public async Task DeleteChat_RefusedByTheEvaluator_AnswersNotFoundOnADryRunToo()
        {
            // The preview is a read of the chat's bindings: it gets no sight beyond the
            // applied delete's.
            var chat = AddChat("Ops alerts", global: false);
            _world.RefuseChatWrites(ApiTokenAuthorization.NotFound);

            var result = await Service.DeleteChat(chat.Id, Token, dryRun: true);

            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            Assert.False(result.IsDryRun);
        }
    }
}
