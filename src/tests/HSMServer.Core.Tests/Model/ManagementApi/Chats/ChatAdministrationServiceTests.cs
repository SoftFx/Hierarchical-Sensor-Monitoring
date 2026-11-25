using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1825: the chat write engine behind the web flows — seam A of the agreed test plan.
    // The storage managers are mocked: these tests pin the engine's orchestration rules
    // (add-or-update idempotency, partial updates with clear flags, webhook URL validation,
    // the managed-folder binding rule, the initiator on every write). The managers' own
    // semantics live in ChatsManagerTests / ChatFolderBindingTests.
    public sealed class ChatAdministrationServiceTests
    {
        private static readonly Guid AuthorId = Guid.NewGuid();

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly InitiatorInfo _initiator = InitiatorInfo.AsUser("admin");

        private ChatAdministrationService Engine => new(_chats.Object, _folders.Object);


        [Fact]
        public async Task CreateChat_NewChat_PersistsFieldsAuthorAndBindsTheFolder()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            SetupChat(chatId, chat: null);
            SetupFolder(folderA);
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            var update = new ChatUpdate
            {
                Id = chatId,
                Name = "Ops alerts",
                Description = "on-call",
                SendMessages = true,
                MessagesAggregationTimeSec = 30,
                SlackWebhookUrl = "https://hooks.slack.com/services/X",
            };
            var binding = new ChatFolderBinding(Keep: [], Add: [folderA.Id], Managed: [folderA.Id, Guid.NewGuid()]);

            var result = await Engine.CreateChatAsync(update, binding, AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.Is<Chat>(c =>
                c.Id == chatId
                && c.AuthorId == AuthorId
                && c.Name == "Ops alerts"
                && c.Description == "on-call"
                && c.SendMessages
                && c.MessagesAggregationTimeSec == 30
                && c.SlackWebhookUrl == "https://hooks.slack.com/services/X")), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && u.Chats.Contains(chatId))), Times.Once);
        }


        [Fact]
        public async Task CreateChat_RowAlreadyExists_UpdatesInsteadOfAdding()
        {
            var chatId = Guid.NewGuid();
            var existing = new Chat(BuildChatEntity(chatId, telegramBound: true));

            SetupChat(chatId, existing);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var update = new ChatUpdate { Id = chatId, Name = "Renamed" };

            var result = await Engine.CreateChatAsync(update, new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u => u.Id == chatId && u.Name == "Renamed")), Times.Once);
        }


        [Fact]
        public async Task CreateChat_EmptyId_GeneratesTheId_AndBindsTheFoldersToIt()
        {
            // A crafted post without a pre-allocated id gets a server-generated one (the
            // old flow's rule); the folder binding must land on THAT id, not on Guid.Empty.
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            SetupNoChatAnywhere();
            SetupFolder(folderA);
            Guid addedId = Guid.Empty;
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>()))
                .Callback<Chat>(c => addedId = c.Id)
                .ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = "Ops" },
                new ChatFolderBinding(Keep: [], Add: [folderA.Id], Managed: [folderA.Id]), AuthorId, _initiator);

            Assert.True(result.Success);
            Assert.NotEqual(Guid.Empty, addedId);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && u.Chats.Contains(addedId))), Times.Once);
        }


        [Fact]
        public async Task UpdateChat_PassesThePartialUpdateThrough_StampedWithTheInitiator()
        {
            var chatId = Guid.NewGuid();
            var existing = new Chat(BuildChatEntity(chatId));

            SetupChat(chatId, existing);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var update = new ChatUpdate { Id = chatId, Name = "Renamed", ClearSlackWebhook = true };

            var result = await Engine.UpdateChatAsync(update, new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u =>
                u.Id == chatId
                && u.Name == "Renamed"
                && u.ClearSlackWebhook == true
                && u.Initiator == _initiator)), Times.Once);
        }

        [Fact]
        public async Task UpdateChat_MalformedWebhookUrl_IsRejectedBeforeAnyStorageWrite()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));

            var update = new ChatUpdate { Id = chatId, SlackWebhookUrl = "ftp://not-a-webhook" };

            var result = await Engine.UpdateChatAsync(update, new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains(nameof(ChatUpdate.SlackWebhookUrl), result.Failure.Errors.Keys);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_UnknownChat_AnswersNotFound()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, chat: null);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task ClearChannel_MapsToTheSingleFlag_NothingElseChanges()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.ClearChannelAsync(chatId, ChatChannel.Telegram, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u =>
                u.Id == chatId
                && u.ClearTelegramBinding == true
                && u.ClearSlackWebhook == null
                && u.ClearMattermostWebhook == null
                && u.Initiator == _initiator)), Times.Once);
        }


        [Fact]
        public async Task ClearChannel_UnknownChat_AnswersNotFound()
        {
            // The outcome the three web Remove buttons map to their 404.
            var chatId = Guid.NewGuid();
            SetupChat(chatId, chat: null);

            var result = await Engine.ClearChannelAsync(chatId, ChatChannel.Slack, _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
        }


        [Fact]
        public async Task UpdateChat_FolderBinding_RemovesOnlyManagedFolders_UnmanagedKeepsItsBinding()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // managed
            var folderB = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // NOT managed

            var existing = new Chat(BuildChatEntity(chatId));
            existing.Folders.Add(folderA.Id);
            existing.Folders.Add(folderB.Id);

            SetupChat(chatId, existing);
            SetupFolder(folderA);
            SetupFolder(folderB);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            // The caller manages only folderA and wants the chat in no folder: folderA is
            // unbound, folderB (outside the managed set) keeps its binding untouched.
            var binding = new ChatFolderBinding(Keep: [], Add: [], Managed: [folderA.Id]);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && !u.Chats.Contains(chatId))), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u => u.Id == folderB.Id)), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_FolderBinding_NeverAddsAFolderOutsideTheManagedSet()
        {
            var chatId = Guid.NewGuid();
            var folderC = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            var existing = new Chat(BuildChatEntity(chatId));
            SetupChat(chatId, existing);
            SetupFolder(folderC);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            // The caller asks for folderC but manages no folder at all: nothing is bound.
            var binding = new ChatFolderBinding(Keep: [], Add: [folderC.Id], Managed: []);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_FolderBinding_DesiredListInBothHalves_AddsAndRemovesOnlyManagedFolders()
        {
            // The shape of a caller holding ONE desired list — the API/MCP surface of #1829:
            // the list is passed as both Keep and Add. Among the managed folders the chat ends
            // up bound to exactly the desired ones (B is added, A is removed); the unmanaged
            // folders are never touched, bound (C) or only desired (D) alike.
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // managed, bound, not desired
            var folderB = new FolderModel(BuildFolderEntity(Guid.NewGuid()));          // managed, desired, unbound
            var folderC = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // unmanaged, bound
            var folderD = new FolderModel(BuildFolderEntity(Guid.NewGuid()));          // unmanaged, desired

            var existing = new Chat(BuildChatEntity(chatId));
            existing.Folders.Add(folderA.Id);
            existing.Folders.Add(folderC.Id);

            SetupChat(chatId, existing);
            SetupFolder(folderA);
            SetupFolder(folderB);
            SetupFolder(folderC);
            SetupFolder(folderD);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            List<Guid> desired = [folderB.Id, folderD.Id];
            var binding = new ChatFolderBinding(Keep: desired, Add: desired, Managed: [folderA.Id, folderB.Id]);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderB.Id
                && u.Chats.Contains(chatId))), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && !u.Chats.Contains(chatId))), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u => u.Id == folderC.Id)), Times.Never);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u => u.Id == folderD.Id)), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_StaleKeptRow_UnboundInTheMeantime_IsNotRebound()
        {
            // Two-admin race (#1840 review round 2): B removed the binding while A's form
            // was open; A's stale kept row must NOT re-bind it — kept rows decide removals
            // only, the old SyncFolders rule. Only the newly picked half adds. The kept row
            // that is STILL bound must not be removed either: no folder write at all.
            var chatId = Guid.NewGuid();
            var folderX = new FolderModel(BuildFolderEntity(Guid.NewGuid())); // stale kept, B unbound it
            var folderY = new FolderModel(BuildFolderEntity(Guid.NewGuid())); // kept and still bound

            var existing = new Chat(BuildChatEntity(chatId));
            existing.Folders.Add(folderY.Id);

            SetupChat(chatId, existing);
            SetupFolder(folderX);
            SetupFolder(folderY);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            var binding = new ChatFolderBinding(Keep: [folderX.Id, folderY.Id], Add: [], Managed: [folderX.Id, folderY.Id]);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }


        [Fact]
        public async Task CreateChat_FailedWrite_AnswersConflictAndBindsNoFolders()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            SetupChat(chatId, chat: null);
            SetupFolder(folderA);
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(false);

            var binding = new ChatFolderBinding(Keep: [], Add: [folderA.Id], Managed: [folderA.Id]);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = chatId, Name = "Ops" }, binding, AuthorId, _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }


        // #1829: chat names are unique per server — one rule for the web form and the API,
        // ignoring case and excluding the chat itself.
        [Fact]
        public async Task CreateChat_NameHeldByAnotherChat_IgnoringCase_AnswersInvalid_AndStoresNothing()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, chat: null);
            _chats.Setup(m => m.GetValues()).Returns([new Chat(BuildChatEntity(Guid.NewGuid(), name: "Ops alerts"))]);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = chatId, Name = "OPS ALERTS" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Chat with the same name already exists."], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        // A creation carries no name at all: the name is required there, while a partial
        // update may omit it.
        [Fact]
        public async Task CreateChat_NoName_AnswersInvalid_AndStoresNothing()
        {
            SetupNoChatAnywhere();

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Name is required"], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        [Theory]
        [InlineData("   ", "Name is required")]
        [InlineData("", "Name is required")]
        [InlineData("The name of this chat is far too long for a chat name because it has more than one hundred characters in it, which the form forbids!",
            "Name length should be less than 100.")]
        public async Task CreateChat_NameBreakingARule_AnswersInvalid_AndStoresNothing(string name, string message)
        {
            SetupNoChatAnywhere();

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = name },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([message], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        [Fact]
        public async Task CreateChat_NameWithSurroundingSpaces_IsStoredTrimmed()
        {
            SetupNoChatAnywhere();
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(true);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = "  Ops  " },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.Is<Chat>(c => c.Name == "Ops")), Times.Once);
        }

        // The uniqueness check excludes the chat itself: keeping a chat's own name in
        // another letter case is no clash with it.
        [Fact]
        public async Task UpdateChat_RenameToItsOwnNameInAnotherCase_IsAccepted()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            _chats.Setup(m => m.GetValues()).Returns([new Chat(BuildChatEntity(chatId, name: "Ops"))]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "OPS" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task UpdateChat_RenameToAnotherChatsName_AnswersInvalid_AndChangesNothing()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            _chats.Setup(m => m.GetValues()).Returns(
            [
                new Chat(BuildChatEntity(chatId, name: "Ops")),
                new Chat(BuildChatEntity(Guid.NewGuid(), name: "On-call")),
            ]);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "ON-CALL" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Chat with the same name already exists."], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
        }

        // A partial update that omits the name leaves the name rule alone — an update may
        // rename nothing.
        [Fact]
        public async Task UpdateChat_WithoutName_IsNotCheckedAgainstTheNameRule()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Description = "on-call" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success);
        }


        private static ChatEntity BuildChatEntity(Guid id, bool telegramBound = false, string name = "pre-created") =>
            new()
            {
                Id = id.ToByteArray(),
                Author = AuthorId.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = name,
                SendMessages = Chat.DefaultSendMessages,
                MessagesAggregationTimeSec = Chat.DefaultMessagesAggregationTimeSec,
                TelegramChatId = telegramBound ? 12345L : null,
            };


        // Moq needs a delegate type for a callback with an out parameter.
        private delegate void OutChat(Guid id, out Chat chat);

        private delegate void OutFolder(Guid id, out FolderModel folder);


        private void SetupChat(Guid chatId, Chat chat) =>
            _chats.Setup(m => m.TryGetValue(chatId, out It.Ref<Chat>.IsAny))
                .Callback(new OutChat((Guid _, out Chat found) => found = chat))
                .Returns(chat is not null);

        // For the server-generated-id path: no id is known up front, so every id misses.
        private void SetupNoChatAnywhere() =>
            _chats.Setup(m => m.TryGetValue(It.IsAny<Guid>(), out It.Ref<Chat>.IsAny))
                .Callback(new OutChat((Guid _, out Chat found) => found = null))
                .Returns(false);

        private void SetupFolder(FolderModel folder) =>
            _folders.Setup(m => m.TryGetValue(folder.Id, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolder((Guid _, out FolderModel found) => found = folder))
                .Returns(true);

        private void SetupFolderUpdate(bool ok) =>
            _folders.Setup(m => m.TryUpdate(It.IsAny<FolderUpdate>())).ReturnsAsync(ok);

        private static FolderEntity BuildFolderEntity(Guid id, params Guid[] chatIds) =>
            new()
            {
                Id = id.ToString(),
                DisplayName = "folder-" + id,
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                Chats = chatIds.Select(c => c.ToByteArray()).ToList(),
            };
    }
}
