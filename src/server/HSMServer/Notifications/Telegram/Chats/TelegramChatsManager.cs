using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly ConcurrentDictionary<ChatId, TelegramChat> _telegramChatIds = new();

        private readonly TelegramConfig _config;
        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;

        public TokensManager TokenManager { get; } = new();

        internal string BotName => _config.BotName;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb =>
            chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        public TelegramChatsManager(IDatabaseCore database, ITreeValuesCache cache, IUserManager userManager,
            IServerConfig config)
        {
            _cache = cache;
            _database = database;
            _config = config.Telegram;
            _userManager = userManager;
        }


        public async override Task<bool> TryAdd(TelegramChat model) =>
            await base.TryAdd(model) && _telegramChatIds.TryAdd(model.ChatId, model);

        public async override Task<bool> TryRemove(RemoveRequest remove) =>
            TryGetValue(remove.Id, out var chat) && await base.TryRemove(remove) && _telegramChatIds.TryRemove(chat.ChatId, out _);


        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, chat) in this)
            {
                if (_userManager.TryGetValueById(chat.AuthorId, out var author))
                    chat.Author = author.Name;

                _telegramChatIds.TryAdd(chat.ChatId, chat);
            }
        }

        public string GetChatName(Guid id) => this.GetValueOrDefault(id)?.Name;

        public TelegramChat GetChatByChatId(ChatId chatId) => _telegramChatIds.GetValueOrDefault(chatId);

        public string GetInvitationLink(Guid folderId, User user) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetGroupInvitation(Guid folderId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(folderId, user)}";

        public async Task<string> TryConnect(Message message, InvitationToken token)
        {
            var isChatExist = _telegramChatIds.TryGetValue(message.Chat, out var chat);

            if (!isChatExist)
            {
                bool isUserChat = message?.Chat?.Type == ChatType.Private;

                chat = new TelegramChat()
                {
                    ChatId = message.Chat,
                    AuthorId = token.User.Id,
                    Author = token.User.Name,
                    AuthorizationTime = DateTime.UtcNow,
                    Type = isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup,
                };

                chat.Update(new TelegramChatUpdate()
                {
                    Id = chat.Id,

                    Name = isUserChat ? message.From.Username : message.Chat.Title,
                    Description = message.Chat.Description,
                });
            }

            var folderName = await ConnectChatToFolder?.Invoke(chat.Id, token.FolderId, token.User.Name);

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

        public async Task RemoveFolderFromChats(Guid folderId, List<Guid> chats, InitiatorInfo initiator)
        {
            foreach (var chatId in chats)
                if (TryGetValue(chatId, out var chat))
                {
                    chat.Folders.Remove(folderId);

                    if (chat.Folders.Count == 0)
                        await TryRemove(new(chatId, initiator));
                }

            _cache.RemoveChatsFromPolicies(folderId, chats, initiator);
        }

        public void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator) =>
            _ = RemoveFolderFromChats(folder.Id, folder.TelegramChats.ToList(), initiator);


        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);
    }
}