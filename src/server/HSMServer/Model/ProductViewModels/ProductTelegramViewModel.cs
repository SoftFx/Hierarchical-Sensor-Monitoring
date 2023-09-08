using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public sealed class ProductTelegramViewModel
    {
        public List<TelegramChat> ConnectedChats { get; } = new();

        public List<TelegramChat> ChatsToAdd { get; } = new();


        public List<Guid> ConnectedChatIds { get; set; }

        public List<Guid> NewChats { get; set; }


        public ProductTelegramViewModel(ProductNodeViewModel product, List<TelegramChat> telegramChats)
        {
            foreach (var chat in telegramChats)
            {
                if (product.TelegramChats.Contains(chat.Id))
                    ConnectedChats.Add(chat);
                else
                    ChatsToAdd.Add(chat);
            }

            ConnectedChats = ConnectedChats.OrderByDescending(ch => ch.Type).ThenBy(ch => ch.Name).ToList();
            ChatsToAdd = ChatsToAdd.OrderBy(ch => ch.Name).ToList();
        }
    }
}
