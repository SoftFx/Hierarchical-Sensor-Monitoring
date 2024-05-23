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

        public bool IsAllChats { get; }
    }


    public enum PolicyDestinationMode : byte
    {
        FromParent = 0,
        Empty,
        NotInitialized,
        Custom = 100,
        AllChats = 200
    }


    public sealed class PolicyDestination : IPolicyDestinationHandler
    {
        public Dictionary<Guid, string> Chats { get; } = [];


        public PolicyDestinationMode Mode { get; private set; }


        public bool IsNotInitialized => Mode is PolicyDestinationMode.NotInitialized;

        public bool IsFromParentChats => Mode is PolicyDestinationMode.FromParent;

        public bool IsAllChats => Mode is PolicyDestinationMode.AllChats;

        public bool IsCustom => Mode is PolicyDestinationMode.Custom;

        public bool IsEmpty => Mode is PolicyDestinationMode.Empty;


        internal PolicyDestination() { }

        internal PolicyDestination(PolicyDestinationEntity entity)
        {
            if (entity.Chats is not null)
                foreach (var (chatId, name) in entity.Chats)
                    Chats.Add(new Guid(chatId), name);

            Mode = entity switch
            {
                { UseDefaultChats: true } => PolicyDestinationMode.FromParent,
                { AllChats: true } => PolicyDestinationMode.AllChats,
                { IsEmpty: true } => PolicyDestinationMode.Empty,
                { Chats.Count: > 0 } => PolicyDestinationMode.Custom,
                _ => PolicyDestinationMode.NotInitialized,
            };
        }


        internal void Update(PolicyDestinationUpdate update)
        {
            Mode = update.Mode ?? Mode;

            Chats.Clear();

            if (update.Chats is not null)
            {
                foreach (var (chatId, name) in update.Chats)
                    Chats.Add(chatId, name);

                if (Chats.Count > 0)
                    Mode = PolicyDestinationMode.Custom;
            }
        }

        internal PolicyDestinationEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            IsNotInitialized = IsNotInitialized,
            UseDefaultChats = IsFromParentChats,
            AllChats = IsAllChats,
            IsEmpty = IsEmpty,
        };

        public override string ToString() => Mode switch
        {
            PolicyDestinationMode.FromParent => "from parent chats",
            PolicyDestinationMode.Empty => "empty destination",
            PolicyDestinationMode.Custom => string.Join(", ", Chats.Values),
            PolicyDestinationMode.AllChats => "all chats",
            _ => "not initialized destination",
        };
    }
}