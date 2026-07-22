using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications.Chats
{
    public sealed class ChatsManager : ConcurrentStorage<Chat, ChatEntity, ChatUpdate>, IChatsManager
    {
        private readonly ConcurrentDictionary<ChatId, Chat> _telegramChatIds = new();

        private readonly TelegramConfig _config;
        private readonly IDatabaseCore _database;
        private readonly IUserManager _userManager;

        public TokensManager TokenManager { get; } = new();

        internal string BotName => _config.BotName;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        protected override Action<ChatEntity> AddToDb => _database.AddChat;

        protected override Action<ChatEntity> UpdateInDb => _database.UpdateChat;

        protected override Action<Chat> RemoveFromDb => chat => _database.RemoveChat(chat.Id.ToByteArray());

        protected override Func<List<ChatEntity>> GetFromDb => _database.GetChats;


        public event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        public ChatsManager(IDatabaseCore database, IUserManager userManager, IServerConfig config)
        {
            _database = database;
            _config = config.Telegram;
            _userManager = userManager;
        }


        public async override Task<bool> TryAdd(Chat model)
        {
            // Pre-flight the Telegram chat-id index so a collision rejects before any base mutation.
            // Without this, base.TryAdd could succeed and _telegramChatIds.TryAdd then fail, leaving
            // the chat in storage but invisible to GetChatByChatId — a ghost chat that survives restart.
            if (model.TelegramChatId is not null && !_telegramChatIds.TryAdd(model.TelegramChatId, model))
                return false;

            if (!await base.TryAdd(model))
            {
                if (model.TelegramChatId is not null)
                    _telegramChatIds.TryRemove(model.TelegramChatId, out _);
                return false;
            }

            return true;
        }

        public async override Task<bool> TryRemove(RemoveRequest remove) =>
            TryGetValue(remove.Id, out var chat) && await base.TryRemove(remove)
            && (chat.TelegramChatId is null || _telegramChatIds.TryRemove(chat.TelegramChatId, out _));

        public override async Task<bool> TryUpdate(ChatUpdate update)
        {
            TryGetValue(update.Id, out var chat);
            var oldTelegramChatId = chat?.TelegramChatId;

            var result = await base.TryUpdate(update);

            if (result && chat is not null && !Nullable.Equals(oldTelegramChatId, chat.TelegramChatId))
            {
                if (oldTelegramChatId is not null)
                    _telegramChatIds.TryRemove(oldTelegramChatId, out _);
                if (chat.TelegramChatId is not null)
                    _telegramChatIds[chat.TelegramChatId] = chat;
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

        public string GetInvitationLink(Guid folderId, User user) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetGroupInvitation(Guid folderId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetChatInvitationLink(Guid chatId, User user) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(chatId, Guid.Empty, user)}";

        public string GetChatGroupInvitation(Guid chatId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(chatId, Guid.Empty, user)}";

        public async Task<ChatConnectResult> TryConnect(Message message, InvitationToken token)
        {
            // EditChat flow: invite targets a Chat record by guid. Two sub-cases:
            //   - existing record (EditChat on a saved chat) → bind Telegram in place
            //   - pre-allocated guid (Add new chat → form not yet saved) → create the record now
            // Both share the strict conflict policy (refuse rebind / refuse theft).
            if (token.ChatId is Guid chatId && chatId != Guid.Empty)
            {
                if (TryGetValue(chatId, out var existing))
                    return await BindTelegramToChat(existing, message);

                return await CreateChatWithTelegram(chatId, message, token.User);
            }

            // Folder-scoped flow: create a brand-new chat and bind it to the folder named in the token.
            var isChatExist = _telegramChatIds.TryGetValue(message?.Chat, out var chat);

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

                chat.Update(new ChatUpdate()
                {
                    Id = chat.Id,
                    Name = isUserChat ? message.From.Username : message.Chat.Title
                });
            }

            var folderName = ConnectChatToFolder is null
                ? null
                : await ConnectChatToFolder.Invoke(chat.Id, token.FolderId, token.User.Name);

            if (string.IsNullOrEmpty(folderName))
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            if (!isChatExist)
                await TryAdd(chat);

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

            if (_telegramChatIds.TryGetValue(incomingChat, out var owner))
            {
                _logger.Warn($"TryConnect: Telegram chat '{incomingChat.Id}' is already bound to chat '{owner.Id}', refusing theft");
                return new ChatConnectResult(ChatConnectOutcome.Failed);
            }

            bool isUserChat = incomingChat.Type == ChatType.Private;

            var chatEntity = new ChatEntity
            {
                Id = chatId.ToByteArray(),
                Author = user.Id.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = isUserChat ? message.From?.Username : incomingChat.Title,
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

            if (!await TryAdd(chat))
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            return new ChatConnectResult(ChatConnectOutcome.ChatBound, chat.Name);
        }

        // Strict conflict policy: refuse if (a) the target Chat already has a different TelegramChatId,
        // or (b) the incoming Telegram chat is already bound to another Chat record. Admin must
        // explicitly Remove Telegram via EditChat to rebind.
        private async Task<ChatConnectResult> BindTelegramToChat(Chat existing, Message message)
        {
            if (message?.Chat is null)
                return new ChatConnectResult(ChatConnectOutcome.Failed);

            var incomingChat = message.Chat;

            if (existing.TelegramChatId is not null && existing.TelegramChatId != incomingChat)
            {
                _logger.Warn($"TryConnect: chat '{existing.Id}' is already bound to Telegram chat '{existing.TelegramChatId}', refusing rebind to '{incomingChat.Id}'");
                return new ChatConnectResult(ChatConnectOutcome.Failed);
            }

            if (_telegramChatIds.TryGetValue(incomingChat, out var owner) && owner.Id != existing.Id)
            {
                _logger.Warn($"TryConnect: Telegram chat '{incomingChat.Id}' is already bound to chat '{owner.Id}', refusing theft");
                return new ChatConnectResult(ChatConnectOutcome.Failed);
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
            };

            var updated = await TryUpdate(update);
            return updated
                ? new ChatConnectResult(ChatConnectOutcome.ChatBound, existing.Name)
                : new ChatConnectResult(ChatConnectOutcome.Failed);
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
