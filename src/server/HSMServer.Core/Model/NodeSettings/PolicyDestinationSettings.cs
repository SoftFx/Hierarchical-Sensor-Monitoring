using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public enum DefaultChatsMode : byte
    {
        NotInitialized = 0, // alerts to Unconfigurated
        Empty = 1, // alerts without notifications
        Custom = 5, // alerts with custom Chats
        FromParent = 10, // settings from Parent
        FromFolder = 20, // setting from Folder (only for Root products)
        All = 100, // for capability with PolicyDestination
    }


    public sealed class PolicyDestinationSettings : IPolicyDestinationHandler
    {
        public Dictionary<Guid, string> Chats { get; } = [];


        public DefaultChatsMode Mode { get; } = DefaultChatsMode.FromParent;


        public bool IsNotInitialized => Mode is DefaultChatsMode.NotInitialized;

        public bool IsFromParent => Mode is DefaultChatsMode.FromParent;

        public bool IsFromFolder => Mode is DefaultChatsMode.FromFolder;

        public bool AllChats => Mode is DefaultChatsMode.All;


        public PolicyDestinationSettings() : base() { }

        public PolicyDestinationSettings(DefaultChatsMode mode) : this()
        {
            Mode = mode;
        }

        public PolicyDestinationSettings(PolicyDestinationSettingsEntity entity)
        {
            Mode = (DefaultChatsMode)entity.Mode;

            if (entity.Chats is not null)
                foreach (var (chatId, name) in entity.Chats)
                    Chats.Add(new Guid(chatId), name);
        }


        public PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            Mode = (byte)Mode,
        };

        public override string ToString() =>
            IsFromFolder ? $"From folder ({ChatsToList()})" : IsFromParent ? "From parent" : ChatsToList();


        private string ChatsToList() => string.Join(", ", Chats.Values);
    }
}