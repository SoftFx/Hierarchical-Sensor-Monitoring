using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.Policies
{
    public interface IPolicyDestinationHandler
    {
        public Dictionary<Guid, string> Chats { get; }

        public bool AllChats { get; }
    }


    public sealed class PolicyDestination : IPolicyDestinationHandler
    {
        public Dictionary<Guid, string> Chats { get; } = [];


        public bool UseDefaultChats { get; private set; }

        public bool AllChats { get; private set; }


        public bool IsNotInitialized => !UseDefaultChats && !AllChats && Chats.Count == 0;


        internal PolicyDestination() { }

        internal PolicyDestination(PolicyDestinationEntity entity)
        {
            UseDefaultChats = entity.UseDefaultChats;
            AllChats = entity.AllChats;

            if (entity.Chats is not null)
                foreach (var (chatId, name) in entity.Chats)
                    Chats.Add(new Guid(chatId), name);
        }


        internal void Update(PolicyDestinationUpdate update)
        {
            UseDefaultChats = update.UseDefaultChats ?? UseDefaultChats;
            AllChats = update.AllChats ?? AllChats;

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

        public override string ToString() =>
            AllChats ? "all chats" : UseDefaultChats ? "default chat" : string.Join(", ", Chats.Values);
    }
}