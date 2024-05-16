using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.Controls
{
    public enum DefaultChatMode
    {
        [Display(Name = "From parent")]
        FromParent,
        [Display(Name = "Not initialized")]
        NotInitialized,
        Empty,
        Custom,
    }


    public sealed record DefaultChatViewModel : SensorSettingControlBase<DefaultChatViewModel>
    {
        public readonly Guid EmptyValue = Guid.Empty;


        public HashSet<Guid> AvailableChats { get; } = [];

        public DefaultChatMode ChatMode { get; set; }

        public string SelectedChat { get; set; }


        public bool IsModify { get; }

        public bool IsCustom => ChatMode is DefaultChatMode.Custom;

        public bool IsFromParent => ChatMode is DefaultChatMode.FromParent;

        public bool IsNotInitialized => ChatMode is DefaultChatMode.NotInitialized;

        public Guid Chat => Guid.TryParse(SelectedChat, out var chat) ? chat : EmptyValue;


        // public constructor without parameters for post actions
        public DefaultChatViewModel() : base() { }

        internal DefaultChatViewModel(ParentRequest parentRequest) : base(parentRequest) { }

        public DefaultChatViewModel(BaseNodeViewModel node, bool isModify = true) : this(node.DefaultChats._parentRequest)
        {
            if (node.TryGetChats(out var availableChats))
                AvailableChats = availableChats;

            IsModify = isModify;
            ChatMode = node.DefaultChats.ChatMode;
            SelectedChat = node.DefaultChats.SelectedChat;
        }


        public bool IsSelectedChat(TelegramChat chat) => ChatMode is DefaultChatMode.Custom && Chat == chat.Id;

        public bool IsSelectedMode(DefaultChatMode mode) => ChatMode == mode;

        public (Guid id, DefaultChatMode mode) GetCurrentChat() => IsFromParent ? GetUsedValue(Parent) : (Chat, ChatMode);

        public string GetCurrentDisplayValue(List<TelegramChat> chatList, out List<TelegramChat> allChats)
        {
            var chats = ToAvailableChats(chatList);
            var (usedChatId, usedMode) = GetCurrentChat();
            var chatName = usedMode switch
            {
                DefaultChatMode.NotInitialized => DefaultChatMode.NotInitialized.GetDisplayName(),
                DefaultChatMode.Empty => DefaultChatMode.Empty.GetDisplayName(),
                _ => chats.TryGetValue(usedChatId, out var chat) ? chat.Name : string.Empty
            };

            allChats = [.. chats.Values];

            return IsFromParent ? AsFromParent(chatName) : chatName;
        }

        public string GetParentDisplayValue(List<TelegramChat> chats)
        {
            var (id, mode) = GetUsedValue(Parent);
            var chatName = mode switch
            {
                DefaultChatMode.Empty => DefaultChatMode.Empty.GetDisplayName(),
                DefaultChatMode.NotInitialized => DefaultChatMode.NotInitialized.GetDisplayName(),
                _ => ToAvailableChats(chats).TryGetValue(id, out var chat) ? chat.Name : string.Empty
            };

            return AsFromParent(chatName);
        }


        internal DefaultChatViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChat = model.Chats.FirstOrDefault().Key.ToString();
            ChatMode = model.Mode switch
            {
                DefaultChatsMode.FromParent or DefaultChatsMode.FromFolder => DefaultChatMode.FromParent,
                DefaultChatsMode.Custom => DefaultChatMode.Custom,
                DefaultChatsMode.Empty => DefaultChatMode.Empty,
                _ => DefaultChatMode.NotInitialized,
            };


            return this;
        }

        internal PolicyDestinationSettings ToModel(Dictionary<Guid, string> availableChats) => new(ToEntity(availableChats));

        internal PolicyDestinationSettingsEntity ToEntity(Dictionary<Guid, string> availableChats)
        {
            var chats = new Dictionary<string, string>(1);

            if (availableChats.TryGetValue(Chat, out var chatName))
                chats.Add($"{SelectedChat}", chatName);

            return new()
            {
                Chats = chats,
                Mode = (byte)(ChatMode switch
                {
                    DefaultChatMode.FromParent => DefaultChatsMode.FromParent,
                    DefaultChatMode.Custom => DefaultChatsMode.Custom,
                    DefaultChatMode.Empty => DefaultChatsMode.Empty,
                    _ => DefaultChatsMode.NotInitialized
                }),
            };
        }

        internal static PolicyDestinationSettingsEntity FromFolderEntity(Dictionary<string, string> chats) =>
            new()
            {
                Mode = (byte)DefaultChatsMode.FromFolder,
                Chats = chats,
            };

        internal PolicyDestinationSettings ToUpdate(ProductNodeViewModel product, ITelegramChatsManager chatsManager, IFolderManager folderManager) =>
            IsFromParent && product.ParentIsFolder
                ? new(FromFolderEntity(folderManager.GetFolderDefaultChat(product.FolderId.Value)))
                : ToModel(product.GetAvailableChats(chatsManager));


        private Dictionary<Guid, TelegramChat> ToAvailableChats(List<TelegramChat> chats) =>
            chats.Where(u => AvailableChats.Contains(u.Id)).ToDictionary(k => k.Id, v => v);

        private static (Guid id, DefaultChatMode mode) GetUsedValue(DefaultChatViewModel model)
        {
            if (model is not null && model.IsFromParent && model.HasParent)
                return GetUsedValue(model.Parent);

            return (model?.Chat ?? Guid.Empty, model?.ChatMode ?? DefaultChatMode.NotInitialized);
        }

        private static string AsFromParent(string chatName) => $"{DefaultChatMode.FromParent.GetDisplayName()} ({chatName})";
    }
}
