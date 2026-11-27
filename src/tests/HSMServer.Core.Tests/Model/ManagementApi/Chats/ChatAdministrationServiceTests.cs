using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.Notifications;
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
            "Name length should be at most 100 characters.")]
        public async Task CreateChat_NameBreakingARule_AnswersInvalid_AndStoresNothing(string name, string message)
        {
            SetupNoChatAnywhere();

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = name },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([message], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        // #1934: the limit includes its own value — a name of exactly the maximum is stored,
        // which is what the message says ("at most").
        [Theory]
        [InlineData(ChatNameValidator.NameMaxLength, true)]
        [InlineData(ChatNameValidator.NameMaxLength + 1, false)]
        public async Task CreateChat_NameOfTheMaximumLength_IsAccepted_TheNextCharacterIsNot(int length, bool accepted)
        {
            SetupNoChatAnywhere();
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(true);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = new string('n', length) },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(accepted, result.Success);
            _chats.Verify(m => m.TryAdd(It.Is<Chat>(c => c.Name.Length == length)), accepted ? Times.Once() : Times.Never());
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


        // The rule guards changes, not history: chats duplicated before names were unique
        // (two Telegram groups under one title, say) keep their other settings editable
        // while they keep their names — the web form always posts the name, even unchanged.
        [Fact]
        public async Task UpdateChat_KeepingANameAnotherChatAlsoHolds_IsAccepted()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops alerts")));
            _chats.Setup(m => m.GetValues()).Returns(
            [
                new Chat(BuildChatEntity(chatId, name: "Ops alerts")),
                new Chat(BuildChatEntity(Guid.NewGuid(), name: "OPS ALERTS")),
            ]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "Ops alerts", Description = "on-call" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success);
        }

        [Fact]
        public async Task CreateChat_OnAPreCreatedRowThatKeepsANameAnotherChatHolds_IsAccepted()
        {
            // The Add-chat form saving a row the /start flow pre-created.
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Dev team")));
            _chats.Setup(m => m.GetValues()).Returns(
            [
                new Chat(BuildChatEntity(chatId, name: "Dev team")),
                new Chat(BuildChatEntity(Guid.NewGuid(), name: "Dev team")),
            ]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = chatId, Name = "Dev team" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        // A row stored with a padded name, from before the name was held trimmed, is the
        // same name once trimmed. The web form shows it trimmed and posts it back on an
        // unchanged save: that is no change. The row then keeps its name as it is stored —
        // it is neither refused as a clash with another chat's name nor renamed behind the
        // user's back (and journaled as a rename).
        [Fact]
        public async Task UpdateChat_PaddedNameKeptOnAnUnchangedSave_WhileAnotherChatHoldsItTrimmed_IsAccepted_AndNotRenamed()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Dev team ")));
            _chats.Setup(m => m.GetValues()).Returns(
            [
                new Chat(BuildChatEntity(chatId, name: "Dev team ")),
                new Chat(BuildChatEntity(Guid.NewGuid(), name: "dev team")),
            ]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "Dev team", Description = "on-call" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success, result.Failure?.Message);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u => u.Id == chatId && u.Name == "Dev team " && u.Description == "on-call")),
                Times.Once);
        }

        [Fact]
        public async Task UpdateChat_PaddedNameKeptOnAnUnchangedSave_IsNotSilentlyRenamed()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Dev team ")));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "Dev team" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success, result.Failure?.Message);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u => u.Id == chatId && u.Name == "Dev team ")), Times.Once);
        }

        [Fact]
        public async Task CreateChat_OnAPreCreatedRowWithAPaddedName_KeptOnSave_IsNotRenamed()
        {
            // The Add-chat form saving a row the /start flow pre-created before names were trimmed.
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Dev team ")));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = chatId, Name = "Dev team" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.True(result.Success, result.Failure?.Message);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u => u.Id == chatId && u.Name == "Dev team ")), Times.Once);
        }

        // The other side of the trim: another chat's padded name is that name, so a rename
        // onto it is the clash it looks like.
        [Fact]
        public async Task UpdateChat_RenameOntoAnotherChatsNamePaddedWithSpaces_IsRefused_AndChangesNothing()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            _chats.Setup(m => m.GetValues()).Returns(
            [
                new Chat(BuildChatEntity(chatId, name: "Ops")),
                new Chat(BuildChatEntity(Guid.NewGuid(), name: "Dev team ")),
            ]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "Dev team" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([ChatNameValidator.NameDuplicateMessage], result.Failure.Errors[nameof(ChatUpdate.Name)]);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
        }


        // The engine checks a name against a snapshot and writes after, so two creates of
        // one name can both pass the check. The store repeats the rule in front of its write
        // and refuses the loser; by then the winner is in the store, and the loser gets the
        // same 422 as up front — the field and the rule, not an opaque conflict.
        [Fact]
        public async Task CreateChat_NameTakenBetweenTheCheckAndTheWrite_AnswersInvalid_UnderName()
        {
            var stored = new List<Chat>();
            _chats.Setup(m => m.GetValues()).Returns(stored);
            SetupNoChatAnywhere();
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>()))
                // The other create lands as this write is refused.
                .Callback<Chat>(_ => stored.Add(new Chat(BuildChatEntity(Guid.NewGuid(), name: "Ops"))))
                .ReturnsAsync(false);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = "Ops" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([ChatNameValidator.NameDuplicateMessage], result.Failure.Errors[nameof(ChatUpdate.Name)]);
        }

        [Fact]
        public async Task UpdateChat_NameTakenBetweenTheCheckAndTheWrite_AnswersInvalid_UnderName()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            var stored = new List<Chat> { new(BuildChatEntity(chatId, name: "Ops")) };
            _chats.Setup(m => m.GetValues()).Returns(stored);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>()))
                .Callback<ChatUpdate>(_ => stored.Add(new Chat(BuildChatEntity(Guid.NewGuid(), name: "New"))))
                .ReturnsAsync(false);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "New" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal([ChatNameValidator.NameDuplicateMessage], result.Failure.Errors[nameof(ChatUpdate.Name)]);
        }

        // A refused write whose name is still free is not a lost race: the store's own
        // failure stays the fixed-message conflict.
        [Fact]
        public async Task UpdateChat_FailedWrite_WithTheNameStillFree_AnswersConflict()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: "Ops")));
            _chats.Setup(m => m.GetValues()).Returns([new Chat(BuildChatEntity(chatId, name: "Ops"))]);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(false);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = "New" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            Assert.Equal("The chat could not be saved.", result.Failure.Message);
        }

        [Fact]
        public async Task UpdateChat_OverlongNameOfAnOldRow_IsEditableThroughTheEngineUntilRenamed()
        {
            // Telegram allows group titles far beyond 100 characters: a row named after
            // one before the /start flow cut them keeps working through the engine — the
            // management API — while it keeps its name, and meets the length rule only
            // when someone renames it. (The web form asks for a shorter name first: its
            // own length check runs before the engine, as it always did.)
            var chatId = Guid.NewGuid();
            var longTitle = new string('t', 120);
            SetupChat(chatId, new Chat(BuildChatEntity(chatId, name: longTitle)));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var kept = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = longTitle, Description = "on-call" },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);
            var renamed = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, Name = new string('u', 120) },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(kept.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, renamed.Failure.Outcome);
            Assert.Equal([ChatNameValidator.NameLengthMessage], renamed.Failure.Errors[nameof(ChatUpdate.Name)]);
        }

        // A negative aggregation delay never flushes a channel's accumulator (the channels
        // send at once only for 0): stored, it would drop the chat's alerts in silence. The
        // rule is the engine's, so the web form and the management API share it.
        [Theory]
        [InlineData(-1)]
        [InlineData(86_401)]
        public async Task CreateChat_DelayOutsideTheRange_AnswersInvalid_AndStoresNothing(int delay)
        {
            SetupNoChatAnywhere();

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = Guid.Empty, Name = "Ops", MessagesAggregationTimeSec = delay },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), AuthorId, _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Equal(["Messages delay must be between 0 and 86400 seconds."],
                result.Failure.Errors[nameof(ChatUpdate.MessagesAggregationTimeSec)]);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_NegativeDelay_AnswersInvalid_AndChangesNothing()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, MessagesAggregationTimeSec = -1 },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(86_400)]
        public async Task UpdateChat_DelayAtTheEdgesOfTheRange_IsAccepted(int delay)
        {
            // 0 sends every alert at once; a day is the ceiling.
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId, MessagesAggregationTimeSec = delay },
                new ChatFolderBinding(Keep: [], Add: [], Managed: []), _initiator);

            Assert.True(result.Success);
        }

        // One rule, one text, for the delay too: the form's Range attribute formats the
        // engine's message template from the same bounds.
        [Fact]
        public void TheWebFormAndTheEngine_SayTheSameAboutTheDelay()
        {
            var range = typeof(ChatViewModel).GetProperty(nameof(ChatViewModel.MessagesDelay))?.GetCustomAttribute<RangeAttribute>();

            Assert.Equal(Chat.MinMessagesAggregationTimeSec, Convert.ToInt32(range.Minimum));
            Assert.Equal(Chat.MaxMessagesAggregationTimeSec, Convert.ToInt32(range.Maximum));
            Assert.Equal(Chat.AggregationTimeRangeText, range.FormatErrorMessage("Messages delay"));
        }

        // The form's own validation, run the way MVC runs it (NotificationsController has
        // no harness of its own): an out-of-range delay is refused before the engine, with
        // the engine's text; a delay at the edges passes; the name is held trimmed.
        [Theory]
        [InlineData(-1)]
        [InlineData(86_401)]
        public void TheWebForm_RefusesAnOutOfRangeDelay_WithTheEnginesText(int delay)
        {
            var model = new ChatViewModel { Name = "Ops alerts", MessagesDelay = delay };
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

            Assert.False(isValid);
            var refusal = Assert.Single(results);
            Assert.Equal([nameof(ChatViewModel.MessagesDelay)], refusal.MemberNames);
            Assert.Equal(Chat.AggregationTimeRangeText, refusal.ErrorMessage);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(86_400)]
        public void TheWebForm_AcceptsADelayAtTheEdges_OfTheRange(int delay)
        {
            var model = new ChatViewModel { Name = "  Ops alerts  ", MessagesDelay = delay };
            var results = new List<ValidationResult>();

            var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

            Assert.True(isValid);
            Assert.Empty(results);
            Assert.Equal("Ops alerts", model.Name);
        }

        // One rule, one text: the form formats its length message from the attribute's
        // arguments and holds the name trimmed (the length rule measures the same value
        // the engine measures); the engine carries the text as a constant.
        [Fact]
        public void TheWebFormAndTheEngine_SayTheSameAboutTheName()
        {
            var formName = typeof(ChatViewModel).GetProperty(nameof(ChatViewModel.Name));
            var length = formName.GetCustomAttribute<StringLengthAttribute>();

            Assert.Equal(ChatNameValidator.NameMaxLength, length.MaximumLength);
            Assert.Equal(ChatNameValidator.NameLengthMessage, length.FormatErrorMessage("Name"));
            Assert.Equal(ChatNameValidator.NameRequiredMessage, formName.GetCustomAttribute<RequiredAttribute>().ErrorMessage);
            Assert.Equal("Ops alerts", new ChatViewModel { Name = "  Ops alerts  " }.Name);
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
