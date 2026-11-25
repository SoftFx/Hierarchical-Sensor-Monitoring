using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications.Chats
{
    public sealed class ChatsManager : ConcurrentStorage<Chat, ChatEntity, ChatUpdate>, IChatsManager
    {
        private readonly ConcurrentDictionary<ChatId, Chat> _telegramChatIds = new();

        // One gate for every write that names a chat. The name rule (#1829) is checked by
        // the engine against a snapshot, and the write comes after: two creates of one name
        // could both pass the check. The uniqueness part of the rule is repeated here, under
        // the gate and in front of the write, so the second of them is refused — TryAdd and
        // TryUpdate answer false, which the engine tells apart from a storage failure.
        // Nothing under the gate raises an event of this storage, so no subscriber can lead
        // a caller back into it.
        private readonly SemaphoreSlim _nameWrites = new(1, 1);

        private readonly TelegramConfig _config;
        private readonly IDatabaseCore _database;
        private readonly IUserManager _userManager;
        private readonly JournalAppender _journal;

        public TokensManager TokenManager { get; } = new();

        internal string BotName => _config.BotName;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        protected override Action<ChatEntity> AddToDb => _database.AddChat;

        protected override Action<ChatEntity> UpdateInDb => _database.UpdateChat;

        protected override Action<Chat> RemoveFromDb => chat => _database.RemoveChat(chat.Id.ToByteArray());

        protected override Func<List<ChatEntity>> GetFromDb => _database.GetChats;


        public event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        public ChatsManager(IDatabaseCore database, IUserManager userManager, IServerConfig config, JournalAppender journal)
        {
            _database = database;
            _config = config.Telegram;
            _userManager = userManager;
            _journal = journal;
        }


        public async override Task<bool> TryAdd(Chat model)
        {
            // Pre-flight the Telegram chat-id index so a collision rejects before any base mutation.
            // Without this, base.TryAdd could succeed and _telegramChatIds.TryAdd then fail, leaving
            // the chat in storage but invisible to GetChatByChatId — a ghost chat that survives restart.
            if (model.TelegramChatId is not null && !_telegramChatIds.TryAdd(model.TelegramChatId, model))
                return false;

            bool added;
            await _nameWrites.WaitAsync();
            try
            {
                added = !ChatNameValidator.IsTaken(model.Name, model.Id, Values) && await AddCore(model);
            }
            finally
            {
                _nameWrites.Release();
            }

            if (!added && model.TelegramChatId is not null)
                _telegramChatIds.TryRemove(model.TelegramChatId, out _);

            return added;
        }

        // Stores a chat the Telegram /start flow creates, under the first name free at the
        // moment of the write. The name would otherwise be picked before the add and checked
        // again inside it: two groups sharing a title, starting at once, could both pick the
        // first free name, and the second add would then be refused.
        private async Task<bool> TryAddUnderAFreeName(Chat chat, string proposed)
        {
            if (chat.TelegramChatId is not null && !_telegramChatIds.TryAdd(chat.TelegramChatId, chat))
                return false;

            bool added;
            await _nameWrites.WaitAsync();
            try
            {
                chat.Update(new ChatUpdate { Id = chat.Id, Name = TelegramChatName(proposed) });
                added = await AddCore(chat);
            }
            finally
            {
                _nameWrites.Release();
            }

            if (!added && chat.TelegramChatId is not null)
                _telegramChatIds.TryRemove(chat.TelegramChatId, out _);

            return added;
        }

        private async Task<bool> AddCore(Chat model)
        {
            if (!await base.TryAdd(model))
                return false;

            _journal.Write(model.Id, JournalKinds.Chat, ChatJournal.CreateEnviroment, model.Name, AuthorOf(model),
                ChatJournal.CreationChanges(ChatJournal.StateOf(model)));

            return true;
        }

        public async override Task<bool> TryRemove(RemoveRequest remove)
        {
            if (!TryGetValue(remove.Id, out var chat) || !await base.TryRemove(remove))
                return false;

            _journal.Write(chat.Id, JournalKinds.Chat, ChatJournal.RemoveEnviroment, chat.Name, remove.Initiator,
                [new("chat", chat.Name, "")]);

            return chat.TelegramChatId is null || _telegramChatIds.TryRemove(chat.TelegramChatId, out _);
        }

        public override async Task<bool> TryUpdate(ChatUpdate update)
        {
            TryGetValue(update.Id, out var chat);
            var oldTelegramChatId = chat?.TelegramChatId;
            var before = chat is null ? default : ChatJournal.StateOf(chat);

            bool result;
            await _nameWrites.WaitAsync();
            try
            {
                // The same last word for a rename. A name the chat already holds is none:
                // the duplicates of the past keep their names and stay editable.
                result = !NamesOntoATakenName(update.Name, update.Id, chat?.Name) && await base.TryUpdate(update);
            }
            finally
            {
                _nameWrites.Release();
            }

            if (result && chat is not null)
            {
                if (!Nullable.Equals(oldTelegramChatId, chat.TelegramChatId))
                {
                    if (oldTelegramChatId is not null)
                        _telegramChatIds.TryRemove(oldTelegramChatId, out _);
                    if (chat.TelegramChatId is not null)
                        _telegramChatIds[chat.TelegramChatId] = chat;
                }

                _journal.Write(chat.Id, JournalKinds.Chat, ChatJournal.UpdateEnviroment, chat.Name,
                    update.Initiator, ChatJournal.ChangesBetween(before, ChatJournal.StateOf(chat)));
            }

            return result;
        }


        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, chat) in this)
            {
                if (_userManager.TryGetValueById(chat.AuthorId, out var author))
                    chat.Author = author.Name;

                if (chat.TelegramChatId is not null)
                    _telegramChatIds.TryAdd(chat.TelegramChatId, chat);
            }
        }

        public string GetChatName(Guid id) => this.GetValueOrDefault(id)?.Name;

        public Chat GetChatByChatId(ChatId chatId) => _telegramChatIds.GetValueOrDefault(chatId);

        // The initiator of a chat's creation is its author. A fresh row has no slot for the
        // write's initiator, and whoever creates a row is its author: the web flow passes the
        // signed-in user, the /start flow the user who issued the invitation. A row whose
        // author is gone journals under System.
        private InitiatorInfo AuthorOf(Chat chat)
        {
            var name = chat.Author
                ?? (_userManager.TryGetValueById(chat.AuthorId, out var author) ? author.Name : null);

            return name is null ? InitiatorInfo.System : InitiatorInfo.AsUser(name);
        }

        // Test-only seam to inject a stale _telegramChatIds entry — mirrors the production failure
        // mode where a binding was cleared through a path that bypassed TryUpdate. Lets the self-heal
        // tests reproduce the user-facing symptom without relying on a real storage corruption.
        internal void InjectStaleTelegramChatIdIndex_TestOnly(long telegramChatId, Chat owner) =>
            _telegramChatIds[new ChatId(telegramChatId)] = owner;

        public string GetInvitationLink(Guid folderId, User user) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetGroupInvitation(Guid folderId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetChatInvitationLink(Guid chatId, User user) =>
            GetChatInvitationLink(chatId, user, InvitationToken.DefaultExpiration);

        public string GetChatGroupInvitation(Guid chatId, User user) =>
            GetChatGroupInvitation(chatId, user, InvitationToken.DefaultExpiration);

        public string GetChatInvitationLink(Guid chatId, User user, TimeSpan invitationLifetime) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(chatId, Guid.Empty, user, invitationLifetime)}";

        public string GetChatGroupInvitation(Guid chatId, User user, TimeSpan invitationLifetime) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(chatId, Guid.Empty, user, invitationLifetime)}";

        public async Task<ChatConnectResult> TryConnect(Message message, InvitationToken token)
        {
            // EditChat flow: invite targets a Chat record by guid. Two sub-cases:
            //   - existing record (EditChat on a saved chat) → bind Telegram in place
            //   - pre-allocated guid (Add new chat → form not yet saved) → create the record now
            // Both share the strict conflict policy (refuse rebind / refuse theft).
            if (token.ChatId is Guid chatId && chatId != Guid.Empty)
            {
                // The binding is the work of the user who issued the invitation: the /start
                // command is typed in Telegram, but the journal names the issuer.
                if (TryGetValue(chatId, out var existing))
                    return await BindTelegramToChat(existing, message, InitiatorInfo.AsUser(token.User.Name));

                return await CreateChatWithTelegram(chatId, message, token.User);
            }

            // Folder-scoped flow: create a brand-new chat and bind it to the folder named in the token.
            var isChatExist = _telegramChatIds.TryGetValue(message?.Chat, out var chat);
            string proposedName = null;

            if (!isChatExist)
            {
                bool isUserChat = message?.Chat?.Type == ChatType.Private;

                chat = new Chat(message.Chat)
                {
                    AuthorId = token.User.Id,
                    Author = token.User.Name,
                    AuthorizationTime = DateTime.UtcNow,
                    TelegramType = isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup,
                };

                proposedName = isUserChat ? message.From.Username : message.Chat.Title;
            }

            var folderName = ConnectChatToFolder is null
                ? null
                : await ConnectChatToFolder.Invoke(chat.Id, token.FolderId, token.User.Name);

            if (string.IsNullOrEmpty(folderName))
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            if (!isChatExist)
                await TryAddUnderAFreeName(chat, proposedName);

            chat.Folders.Add(token.FolderId);
            return new ChatConnectResult(ChatConnectOutcome.FolderAdded, folderName);
        }

        // Pre-allocated guid flow: AddChat (GET) hands the EditChat form a fresh guid before any row
        // exists in storage. When the user completes /start against that token, we materialise the
        // Chat record here — carrying the pre-allocated guid so the form the user is still looking
        // at remains valid after save. The chat is created with no folder binding (global) and
        // admin-owned by the user who issued the token. Theft guard is shared with BindTelegramToChat.
        private async Task<ChatConnectResult> CreateChatWithTelegram(Guid chatId, Message message, User user)
        {
            if (message?.Chat is null)
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            var incomingChat = message.Chat;

            if (GetActiveTelegramOwner(incomingChat) is { } owner)
            {
                _logger.Warn($"TryConnect: Telegram chat '{incomingChat.Id}' is already bound to chat '{owner.Id}', refusing theft");
                return new ChatConnectResult(ChatConnectOutcome.FailedAlreadyBound, owner.Name ?? owner.Id.ToString());
            }

            bool isUserChat = incomingChat.Type == ChatType.Private;

            var chatEntity = new ChatEntity
            {
                Id = chatId.ToByteArray(),
                Author = user.Id.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                SendMessages = Chat.DefaultSendMessages,
                MessagesAggregationTimeSec = Chat.DefaultMessagesAggregationTimeSec,
                TelegramChatId = incomingChat.Id,
                TelegramType = (byte)(isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup),
                AuthorizationTime = DateTime.UtcNow.Ticks,
            };

            var chat = new Chat(chatEntity)
            {
                Author = user.Name,
            };

            if (!await TryAddUnderAFreeName(chat, isUserChat ? message.From?.Username : incomingChat.Title))
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            return new ChatConnectResult(ChatConnectOutcome.ChatBound, chat.Name);
        }

        // A chat the /start flow creates is named after the Telegram chat — the group
        // title, or the username of a direct chat. Chat names are unique per server
        // (#1829) and two groups may share a title, so a name another chat holds gets
        // " (2)", " (3)"… appended: nobody is there to pick another name.
        private string TelegramChatName(string proposed) =>
            ChatNameValidator.UniqueIn(proposed, GetValues());

        // Whether a write gives a chat a name another chat holds — a rename, which is what
        // the rule guards. A name the chat already holds (trimmed alike: a stored name may
        // be padded) is none, and neither is no name at all.
        private bool NamesOntoATakenName(string name, Guid chatId, string storedName) =>
            name is not null
            && !string.Equals(name.Trim(), storedName?.Trim(), StringComparison.Ordinal)
            && ChatNameValidator.IsTaken(name, chatId, Values);

        // Strict conflict policy: refuse if (a) the target Chat already has a different TelegramChatId
        // (rebind — admin must explicitly Remove Telegram via EditChat first), or (b) the incoming
        // Telegram chat is actively bound to another Chat record (theft — surfaced to the bot reply
        // as FailedAlreadyBound with the owner name). The theft check goes through GetActiveTelegramOwner,
        // which silently drops stale index entries (owner.TelegramChatId cleared or moved) so storage
        // corruption doesn't permanently block legitimate binds.
        private async Task<ChatConnectResult> BindTelegramToChat(Chat existing, Message message, InitiatorInfo initiator)
        {
            if (message?.Chat is null)
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            var incomingChat = message.Chat;

            if (existing.TelegramChatId is not null && existing.TelegramChatId != incomingChat)
            {
                _logger.Warn($"TryConnect: chat '{existing.Id}' is already bound to Telegram chat '{existing.TelegramChatId}', refusing rebind to '{incomingChat.Id}'");
                return new ChatConnectResult(ChatConnectOutcome.Failed);
            }

            if (GetActiveTelegramOwner(incomingChat) is { } owner && owner.Id != existing.Id)
            {
                _logger.Warn($"TryConnect: Telegram chat '{incomingChat.Id}' is already bound to chat '{owner.Id}', refusing theft");
                return new ChatConnectResult(ChatConnectOutcome.FailedAlreadyBound, owner.Name ?? owner.Id.ToString());
            }

            bool isUserChat = incomingChat.Type == ChatType.Private;

            // TelegramType / AuthorizationTime are internal-set and not exposed by ChatUpdate —
            // set them directly before TryUpdate, which persists via ToEntity() reading live state.
            // Description is not on the base Chat type — ChatNamesSynchronization (TelegramBot.cs:255)
            // will fill it on the next pass via GetChat(ChatFullInfo).
            existing.TelegramType = isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup;
            existing.AuthorizationTime = DateTime.UtcNow;

            var update = new ChatUpdate
            {
                Id = existing.Id,
                TelegramChatId = incomingChat.Id,
                TelegramChatTitle = isUserChat ? message.From?.Username : incomingChat.Title,
                Initiator = initiator,
            };

            var updated = await TryUpdate(update);
            return updated
                ? new ChatConnectResult(ChatConnectOutcome.ChatBound, existing.Name)
                : new ChatConnectResult(ChatConnectOutcome.Failed);
        }

        // Lookup the Chat that actively owns `incomingChat` in the _telegramChatIds index. Returns
        // null both when there's no entry at all and when the entry is stale — i.e. the indexed
        // owner's TelegramChatId is null or points elsewhere (storage corruption or a binding
        // cleared through a path that bypassed TryUpdate). Stale entries are dropped silently so
        // future binds are not blocked by dangling state; the caller proceeds as if free.
        private Chat GetActiveTelegramOwner(global::Telegram.Bot.Types.Chat incomingChat)
        {
            if (!_telegramChatIds.TryGetValue(incomingChat, out var owner))
                return null;

            if (owner.TelegramChatId is null || owner.TelegramChatId != incomingChat)
            {
                _logger.Warn($"TryConnect: dropping stale _telegramChatIds entry for Telegram chat '{incomingChat.Id}' (was pointing at chat '{owner.Id}' which no longer claims it)");
                _telegramChatIds.TryRemove(incomingChat, out _);
                return null;
            }

            return owner;
        }

        public void AddFolderToChats(Guid folderId, List<Guid> chats)
        {
            foreach (var chatId in chats)
                if (TryGetValue(chatId, out var chat))
                    chat.Folders.Add(folderId);
        }

        public Task RemoveFolderFromChats(Guid folderId, List<Guid> chats, InitiatorInfo initiator)
        {
            foreach (var chatId in chats)
                if (TryGetValue(chatId, out var chat))
                    chat.Folders.Remove(folderId);

            return Task.CompletedTask;
        }

        public void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator) =>
            _ = RemoveFolderFromChats(folder.Id, folder.Chats.ToList(), initiator);


        protected override Chat FromEntity(ChatEntity entity) => new(entity);

        public async Task MigrateToSupergroup(long oldChatId, long newChatId)
        {
            var chat = GetChatByChatId(oldChatId);

            if (chat is null)
            {
                _logger.Warn($"MigrateToSupergroup: Chat '{oldChatId}' not found");
                return;
            }

            var updated = await TryUpdate(new ChatUpdate
            {
                Id = chat.Id,
                TelegramChatId = newChatId,
            });

            if (updated)
                _logger.Info($"MigrateToSupergroup: Chat '{oldChatId}' was updated to supergroup '{newChatId}'");
        }
    }
}
