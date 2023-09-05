using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly IDatabaseCore _database;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb => chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public TelegramChatsManager(IDatabaseCore database)
        {
            _database = database;
        }


        public void Dispose() { }

        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);
    }
}
