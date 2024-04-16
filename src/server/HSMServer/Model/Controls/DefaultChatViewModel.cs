using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Controls
{
    public class DefaultChatViewModel
    {
        public const string NotInitialized = "Not initialized";

        private readonly ParentRequest _parentRequest;
        public readonly Guid NotInitializedId = Guid.Empty;


        internal delegate (DefaultChatViewModel Value, bool IsFolder) ParentRequest();


        public HashSet<Guid> AvailableChats { get; } = [];


        public Guid? SelectedChat { get; set; }


        internal DefaultChatViewModel ParentValue => _parentRequest?.Invoke().Value;

        public Guid ParentChat => GetUsedValue(ParentValue);

        public bool HasParentValue => ParentValue is not null;

        public bool IsFromParent => SelectedChat is null;


        public DefaultChatViewModel() { }

        internal DefaultChatViewModel(ParentRequest parentRequest)
        {
            _parentRequest = parentRequest;
        }

        public DefaultChatViewModel(BaseNodeViewModel node) : this(node.DefaultChats._parentRequest)
        {
            if (node.TryGetChats(out var availableChats))
                AvailableChats = availableChats;

            SelectedChat = node.DefaultChats.SelectedChat;
        }


        public bool ChatIsSelected(Guid chatId) => SelectedChat == chatId;

        internal DefaultChatViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChat = model.IsFromParent ? null : model.Chats.FirstOrDefault().Key;

            return this;
        }

        internal PolicyDestinationSettings ToModel(Dictionary<Guid, string> availableChats) => new(ToEntity(availableChats));

        internal PolicyDestinationSettingsEntity ToEntity(Dictionary<Guid, string> availableChats)
        {
            var chats = new Dictionary<Guid, string>(1);

            if (SelectedChat.HasValue && availableChats.TryGetValue(SelectedChat.Value, out var chatName))
                chats.Add(SelectedChat.Value, chatName);

            return new()
            {
                Chats = chats.ToDictionary(k => k.Key.ToString(), v => v.Value),
                IsFromParent = IsFromParent,
            };
        }

        private Guid GetUsedValue(DefaultChatViewModel model)
        {
            if (model is not null && model.SelectedChat is null && model.HasParentValue)
                return GetUsedValue(model.ParentValue);

            return model?.SelectedChat ?? NotInitializedId;
        }
    }
}
