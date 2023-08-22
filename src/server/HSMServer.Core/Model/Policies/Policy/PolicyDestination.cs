using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public sealed class PolicyDestination
    {
        public Dictionary<Guid, string> Chats { get; } = new();

        public bool AllChats { get; set; } // TODO: should be private set after policies destination migration


        public PolicyDestination() { } // TODO: should be internal after policies destination migration

        internal PolicyDestination(PolicyDestinationEntity entity)
        {
            if (entity is null)
                return;

            AllChats = entity.AllChats;
            Chats.Clear();

            if (entity.Chats is not null)
                foreach (var (chatId, name) in entity.Chats)
                    Chats.Add(new Guid(chatId), name);
        }


        public override string ToString() => $"chats={(AllChats ? "all chats" : string.Join(", ", Chats.Values))}";


        internal void Update(PolicyDestinationUpdate update)
        {
            if (update is null)
                return;

            AllChats = update.AllChats;
            Chats.Clear();

            if (update.Chats is not null)
                foreach (var (chatId, name) in update.Chats)
                    Chats.Add(chatId, name);
        }

        internal PolicyDestinationEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            AllChats = AllChats,
        };
    }
}
