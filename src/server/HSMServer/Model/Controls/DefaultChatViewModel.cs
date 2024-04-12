using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Controls
{
    public class DefaultChatViewModel
    {
        public const string NotInitialized = "Not initialized";

        private readonly ParentRequest _parentRequest;


        internal delegate (DefaultChatViewModel Value, bool IsFolder) ParentRequest();


        public HashSet<Guid> AvailableChats { get; } = new();


        public Guid SelectedChat { get; set; }

        public bool IsFromParent { get; set; }


        internal DefaultChatViewModel ParentValue => _parentRequest?.Invoke().Value;

        public Guid ParentChat => ParentValue?.SelectedChat ?? Guid.Empty;

        public bool HasParentValue => ParentValue is not null;


        public DefaultChatViewModel() { }

        internal DefaultChatViewModel(ParentRequest parentRequest)
        {
            _parentRequest = parentRequest;
        }

        public DefaultChatViewModel(BaseNodeViewModel node) : this(node.DefaultChat._parentRequest)
        {
            if (node.TryGetChats(out var availableChats))
                AvailableChats = availableChats;
        }


        public bool ChatIsSelected(TelegramChat chat) => SelectedChat == chat.Id;

        internal DefaultChatViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChat = model.Chats.FirstOrDefault().Key;
            IsFromParent = model.IsFromParent;

            return this;
        }
    }
}
