using HSMServer.Model.Controls;
using HSMServer.Notifications.Chats;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderChatsViewModel
    {
        public List<Chat> ConnectedChats { get; } = new();

        public List<Chat> ChatsToAdd { get; } = new();


        public DefaultChatViewModel DefaultChats { get; set; }

        public List<Guid> ConnectedChatIds { get; set; }

        public List<Guid> NewChats { get; set; }

        public Guid FolderId { get; set; }


        public FolderChatsViewModel() { }

        public FolderChatsViewModel(FolderModel folder, List<Chat> chats)
        {
            FolderId = folder.Id;
            DefaultChats = new DefaultChatViewModel(folder);

            foreach (var chat in chats)
            {
                if (folder.Chats.Contains(chat.Id))
                    ConnectedChats.Add(chat);
                else
                    ChatsToAdd.Add(chat);
            }

            ConnectedChats = ConnectedChats
                .OrderByDescending(ch => ch.TelegramType)
                .ThenBy(ch => ch.Name)
                .ToList();

            ChatsToAdd = ChatsToAdd
                .OrderBy(ch => ch.Name)
                .ToList();

            ConnectedChatIds = ConnectedChats.Select(ch => ch.Id).ToList();
        }
    }
}
