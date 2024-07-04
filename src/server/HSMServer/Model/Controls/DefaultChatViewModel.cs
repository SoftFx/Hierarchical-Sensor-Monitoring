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
        public HashSet<Guid> AvailableChats { get; } = [];

        public HashSet<Guid> SelectedChats { get; set; } = [];

        public DefaultChatMode ChatMode { get; set; }


        public bool IsModify { get; }

        public bool IsCustom => ChatMode is DefaultChatMode.Custom;

        public bool IsFromParent => ChatMode is DefaultChatMode.FromParent;

        public bool IsNotInitialized => ChatMode is DefaultChatMode.NotInitialized;
        
        public bool IsEmpty => ChatMode is DefaultChatMode.Empty;


        // public constructor without parameters for post actions
        public DefaultChatViewModel() : base() { }

        internal DefaultChatViewModel(ParentRequest parentRequest) : base(parentRequest) { }

        public DefaultChatViewModel(BaseNodeViewModel node, bool isModify = true) : this(node.DefaultChats._parentRequest)
        {
            if (node.TryGetChats(out var availableChats))
                AvailableChats = availableChats;

            IsModify = isModify;
            ChatMode = node.DefaultChats.ChatMode;
            SelectedChats = new(node.DefaultChats.SelectedChats);
        }


        public bool IsSelectedChat(TelegramChat chat) => ChatMode is DefaultChatMode.Custom or DefaultChatMode.FromParent && SelectedChats.Contains(chat.Id);

        public bool IsSelectedMode(DefaultChatMode mode) => ChatMode == mode;

        public (HashSet<Guid> ids, DefaultChatMode mode) GetCurrentChats() 
        {
            if (!IsFromParent) 
                return (SelectedChats, ChatMode);
            
            if (SelectedChats.Count == 0)
                return GetUsedValue(Parent);
                
            var usedValue = GetUsedValue(Parent);
        
            foreach (var id in SelectedChats)
                usedValue.ids.Add(id);
                    
            return usedValue;
        }

        public string GetAlertTelegramChats(List<TelegramChat> chats)
        {
            return GetCurrentDisplayValue(chats, out _);
        }

        public HashSet<Guid> GetParentChats()
        {
            var chatIds = new HashSet<Guid>(1 << 4);

            chatIds = GetChats(this);
            
            static HashSet<Guid> GetChats(DefaultChatViewModel model)
            {
                if (model is null || model.IsEmpty || model.IsNotInitialized)
                    return [];
                
                var ids = new HashSet<Guid>();
                
                foreach (var id in model.SelectedChats)
                    ids.Add(id);

                if (model.ChatMode == DefaultChatMode.FromParent)
                {
                    foreach (var id in GetChats(model.Parent))
                    {
                        ids.Add(id);
                    }
                }
                
                return ids;
            }
            
            return chatIds;
        }

        public string GetCurrentDisplayValue(List<TelegramChat> chatList, out List<TelegramChat> allChats)
        {
            var chats = ToAvailableChats(chatList);
            allChats = [.. chats.Values];
            
            return ChatMode switch
            {
                DefaultChatMode.FromParent => GetFromParentValue(),
                DefaultChatMode.Custom => SelectedChats.ToNames(chats),
                _ => ChatMode.GetDisplayName()
            };
            
            string GetFromParentValue()
            {
                var parentChats = Parent?.GetParentChats() ?? [];

                var parentValue = string.Empty;
                if ((Parent?.IsNotInitialized ?? false) || (Parent?.IsEmpty ?? false))
                    parentValue = Parent.ChatMode.GetDisplayName();
                else
                    parentValue = parentChats.ToNames(chats);
                
                //SelectedChats.ExceptWith(parentChats);
                
                if (SelectedChats.Count == 0)
                    return  AsFromParent(parentValue) + ", " + SelectedChats.ToNames(chats);
                
                return AsFromParent(parentValue);
            }
        }

        public string GetParentDisplayValue(List<TelegramChat> chats)
        {
            var availableChats = ToAvailableChats(chats);
            var parentIds = Parent?.GetParentChats() ?? [];
            
            var chatsName = Parent?.ChatMode switch
            {
                DefaultChatMode.Empty => DefaultChatMode.Empty.GetDisplayName(),
                DefaultChatMode.NotInitialized => DefaultChatMode.NotInitialized.GetDisplayName(),
                _ => parentIds.ToNames(availableChats),
            };

            if (parentIds.Count == 0)
                chatsName = DefaultChatMode.Empty.GetDisplayName();
            
            return AsFromParent(chatsName);
        }


        internal DefaultChatViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChats.Clear();
            foreach (var (id, _) in model.Chats)
                SelectedChats.Add(id);

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
            
            if (IsFromParent || IsCustom)
                foreach (var chat in SelectedChats)
                    if (availableChats.TryGetValue(chat, out var chatName))
                        chats.Add($"{chat}", chatName);

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

        internal PolicyDestinationSettings ToUpdate(ProductNodeViewModel product, ITelegramChatsManager chatsManager, IFolderManager folderManager)
        {
            return IsFromParent && product.ParentIsFolder
                ? new(FromFolderEntity(folderManager.GetFolderDefaultChats(product.FolderId.Value)))
                : ToModel(product.GetAvailableChats(chatsManager));
        }


        private Dictionary<Guid, TelegramChat> ToAvailableChats(List<TelegramChat> chats) =>
            chats.Where(u => AvailableChats.Contains(u.Id)).ToDictionary(k => k.Id, v => v);

        private static (HashSet<Guid> ids, DefaultChatMode mode) GetUsedValue(DefaultChatViewModel model)
        {
            if (model is not null && model.IsFromParent && model.HasParent)
                return GetUsedValue(model.Parent);

            return (model?.SelectedChats ?? [], model?.ChatMode ?? DefaultChatMode.NotInitialized);
        }

        private static string AsFromParent(string chatName) => $"{DefaultChatMode.FromParent.GetDisplayName()} ({chatName})";
    }
}
