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

        public async Task<string> TryConnect(Message message, InvitationToken token)
        {
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

            if (!string.IsNullOrEmpty(folderName))
            {
                if (!isChatExist)
                    await TryAdd(chat);

                chat.Folders.Add(token.FolderId);
            }

            return folderName;
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
