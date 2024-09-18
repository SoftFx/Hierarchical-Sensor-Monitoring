using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Core.Model.NodeSettings
{
    public enum DefaultChatsMode : byte
    {
        [Display(Name = "Not initialized")]
        NotInitialized = 0, // unconfigured alerts
        Empty = 1, // alerts without notifications
        Custom = 5, // alerts with custom Chats
        [Display(Name = "From parent")]
        FromParent = 10, // settings from Parent
        [Display(Name = "From folder")]
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

        public bool IsAllChats => Mode is DefaultChatsMode.All;


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
                    Chats.TryAdd(new Guid(chatId), name);
        }

        public PolicyDestinationSettings ApplyNewChats(Dictionary<Guid, string> newChats)
        {
            var settings = new PolicyDestinationSettings(Mode is DefaultChatsMode.FromParent ? DefaultChatsMode.FromParent : DefaultChatsMode.Custom);

            foreach (var chat in Chats)
                settings.Chats.TryAdd(chat.Key, chat.Value);

            foreach (var chat in newChats)
                settings.Chats.TryAdd(chat.Key, chat.Value);

            return settings;
        }


        public PolicyDestinationSettingsEntity ToEntity() => new()
        {
            Chats = Chats?.ToDictionary(k => k.Key.ToString(), v => v.Value),
            Mode = (byte)Mode,
        };

        public override string ToString() =>
            Mode switch
            {
                DefaultChatsMode.FromFolder when Chats.Count != 0 => $"{DefaultChatsMode.FromParent.GetDisplayName()}, ${ChatsToList()}",
                DefaultChatsMode.Custom => ChatsToList(),
                DefaultChatsMode.FromParent when Chats.Count != 0 => $"{Mode.GetDisplayName()}, ${ChatsToList()}",
                _ => Mode.GetDisplayName(),
            };


        private string ChatsToList() => string.Join(", ", Chats.Values);
    }
}