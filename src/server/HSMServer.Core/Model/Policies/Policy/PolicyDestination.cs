using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyDestination
    {
        public Dictionary<Guid, string> Chats { get; } = [];


        public bool UseDefaultChats { get; private set; }

        public bool AllChats { get; private set; }


        internal PolicyDestination() { }

        internal PolicyDestination(PolicyDestinationEntity entity)
        {
            UseDefaultChats = entity.UseDefaultChats;
            AllChats = entity.AllChats;

            Chats.Clear();

            if (entity.Chats is not null)
                foreach (var (chatId, name) in entity.Chats)
                    Chats.Add(new Guid(chatId), name);
        }


        internal void Update(PolicyDestinationUpdate update)
        {
            UseDefaultChats = update.UseDefaultChats ?? UseDefaultChats;
            AllChats = update.AllChats;

            Chats.Clear();

            if (update.Chats is not null)
                foreach (var (chatId, name) in update.Chats)
                    Chats.Add(chatId, name);
        }

        internal PolicyDestinationEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            UseDefaultChats = UseDefaultChats,
            AllChats = AllChats,
        };

        public override string ToString()
        {
            if (AllChats)
                return "all chats";
            else
                return UseDefaultChats ? "default charts" : string.Join(", ", Chats.Values);
        }
    }
}