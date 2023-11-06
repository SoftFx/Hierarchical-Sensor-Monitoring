using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderTelegramViewModel
    {
        public List<TelegramChat> ConnectedChats { get; } = new();

        public List<TelegramChat> ChatsToAdd { get; } = new();


        public List<Guid> ConnectedChatIds { get; set; }

        public List<Guid> NewChats { get; set; }

        public Guid FolderId { get; set; }


        public FolderTelegramViewModel() { }

        public FolderTelegramViewModel(FolderModel folder, List<TelegramChat> telegramChats)
        {
            FolderId = folder.Id;

            foreach (var chat in telegramChats)
            {
                if (folder.TelegramChats.Contains(chat.Id))
                    ConnectedChats.Add(chat);
                else
                    ChatsToAdd.Add(chat);
            }

            ConnectedChats = ConnectedChats.OrderByDescending(ch => ch.Type).ThenBy(ch => ch.Name).ToList();
            ChatsToAdd = ChatsToAdd.OrderBy(ch => ch.Name).ToList();

            ConnectedChatIds = ConnectedChats.Select(ch => ch.Id).ToList();
        }
    }
}
