using HSMServer.Model.Controls;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderChatsViewModel
    {
        public List<TelegramChat> ConnectedTelegramChats { get; } = new();

        public List<TelegramChat> TelegramChatsToAdd { get; } = new();

        public List<SlackDestination> ConnectedSlackDestinations { get; } = new();

        public List<SlackDestination> SlackDestinationsToAdd { get; } = new();


        public DefaultChatViewModel DefaultChats { get; set; }

        public List<Guid> ConnectedChatIds { get; set; }

        public List<Guid> NewChats { get; set; }

        public Guid FolderId { get; set; }


        public FolderChatsViewModel() { }

        public FolderChatsViewModel(FolderModel folder, List<TelegramChat> telegramChats, List<SlackDestination> slackDestinations)
        {
            FolderId = folder.Id;
            DefaultChats = new DefaultChatViewModel(folder);

            foreach (var chat in telegramChats)
            {
                if (folder.Chats.Contains(chat.Id))
                    ConnectedTelegramChats.Add(chat);
                else
                    TelegramChatsToAdd.Add(chat);
            }

            foreach (var dest in slackDestinations)
            {
                if (folder.Chats.Contains(dest.Id))
                    ConnectedSlackDestinations.Add(dest);
                else
                    SlackDestinationsToAdd.Add(dest);
            }

            ConnectedTelegramChats = ConnectedTelegramChats.OrderByDescending(ch => ch.Type).ThenBy(ch => ch.Name).ToList();
            TelegramChatsToAdd = TelegramChatsToAdd.OrderBy(ch => ch.Name).ToList();

            ConnectedSlackDestinations = ConnectedSlackDestinations.OrderBy(d => d.Name).ToList();
            SlackDestinationsToAdd = SlackDestinationsToAdd.OrderBy(d => d.Name).ToList();

            ConnectedChatIds = ConnectedTelegramChats.Select(ch => ch.Id)
                .Concat(ConnectedSlackDestinations.Select(d => d.Id))
                .ToList();
        }
    }
}
