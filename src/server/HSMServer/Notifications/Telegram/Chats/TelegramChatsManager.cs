using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly IDatabaseCore _database;
        private readonly IUserManager _userManager;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb => chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public TelegramChatsManager(IDatabaseCore database, IUserManager userManager)
        {
            _database = database;
            _userManager = userManager;
        }


        public void Dispose() { }

        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, chat) in this)
            {
                if (_userManager.TryGetValueById(chat.AuthorId, out var author))
                    chat.Author = author.Name;
            }
        }

        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);
    }
}
