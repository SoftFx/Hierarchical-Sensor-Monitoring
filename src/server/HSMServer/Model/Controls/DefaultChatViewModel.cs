using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Controls
{
    public sealed record DefaultChatViewModel : SensorSettingControlBase<DefaultChatViewModel>
    {
        public static (Guid Id, string Name) EmptyValue { get; } = (Guid.Empty, "Not initialized");


        public HashSet<Guid> AvailableChats { get; } = [];

        public Guid? SelectedChat { get; set; }


        public bool IsModify { get; }

        public bool IsFromParent => SelectedChat is null;


        // public constructor without parameters for post actions
        public DefaultChatViewModel() : base() { }

        internal DefaultChatViewModel(ParentRequest parentRequest) : base(parentRequest) { }

        public DefaultChatViewModel(BaseNodeViewModel node, bool isModify = true) : this(node.DefaultChats._parentRequest)
        {
            if (node.TryGetChats(out var availableChats))
                AvailableChats = availableChats;

            IsModify = isModify;
            SelectedChat = node.DefaultChats.SelectedChat;
        }


        public bool IsSelectedChat(Guid chatId) => SelectedChat == chatId;

        public string GetCurrentDisplayValue(List<TelegramChat> chatList, out List<TelegramChat> allChats)
        {
            var chats = ToAvailableChats(chatList);

            allChats = [.. chats.Values];

            var targetId = IsFromParent ? GetUsedValue(Parent) : SelectedChat.Value;
            var chatName = chats.TryGetValue(targetId, out var chat) ? chat.Name : EmptyValue.Name;

            return IsFromParent ? AsFromParent(chatName) : chatName;
        }

        public string GetParentDisplayValue(List<TelegramChat> chats)
        {
            var usedValue = GetUsedValue(Parent);

            if (usedValue == EmptyValue.Id)
                return AsFromParent(EmptyValue.Name);

            return ToAvailableChats(chats).TryGetValue(usedValue, out var chat) ? AsFromParent(chat.Name) : string.Empty;
        }


        internal DefaultChatViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChat = model.InheritanceMode is DefaultChatInheritanceMode.None ? model.Chats.FirstOrDefault().Key : null;

            return this;
        }

        internal PolicyDestinationSettings ToModel(Dictionary<Guid, string> availableChats, bool parentIsFolder = false) => new(ToEntity(availableChats, parentIsFolder));

        internal PolicyDestinationSettingsEntity ToEntity(Dictionary<Guid, string> availableChats, bool parentIsFolder = false)
        {
            var chats = new Dictionary<string, string>(1);

            if (SelectedChat.HasValue && availableChats.TryGetValue(SelectedChat.Value, out var chatName))
                chats.Add($"{SelectedChat.Value}", chatName);

            return new()
            {
                Chats = chats,
                InheritanceMode = parentIsFolder ? (byte)DefaultChatInheritanceMode.FromFolder :
                                  IsFromParent ? (byte)DefaultChatInheritanceMode.FromParent : (byte)DefaultChatInheritanceMode.None,
            };
        }


        private Dictionary<Guid, TelegramChat> ToAvailableChats(List<TelegramChat> chats) =>
            chats.Where(u => AvailableChats.Contains(u.Id)).ToDictionary(k => k.Id, v => v);

        private static Guid GetUsedValue(DefaultChatViewModel model)
        {
            if (model is not null && model.SelectedChat is null && model.HasParent)
                return GetUsedValue(model.Parent);

            return model?.SelectedChat ?? EmptyValue.Id;
        }

        private static string AsFromParent(string chatName) => $"From parent ({chatName})";
    }
}
