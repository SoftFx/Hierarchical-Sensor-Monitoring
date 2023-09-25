using HSMServer.Authentication;
using HSMServer.Extensions;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.NotificationViewModels
{
    public class TelegramChatViewModel
    {
        public Dictionary<Guid, string> Products { get; } = new();

        public Dictionary<Guid, string> Managers { get; } = new();


        public string AuthorizationTime { get; }

        public ConnectedChatType Type { get; }

        public Guid SystemId { get; }

        public long ChatId { get; }

        public string Name { get; }


        public TelegramChatViewModel(TelegramChat chat, TreeViewModel.TreeViewModel tree, IUserManager userManager)
        {
            SystemId = chat.Id;
            ChatId = chat.ChatId.Identifier ?? 0L;
            Name = chat.Name;
            Type = chat.Type;
            AuthorizationTime = chat.AuthorizationTime == DateTime.MinValue
                ? "-"
                : chat.AuthorizationTime.ToDefaultFormat();

            Initialize(chat, tree, userManager);
        }


        private void Initialize(TelegramChat chat, TreeViewModel.TreeViewModel tree, IUserManager userManager)
        {
            foreach (var productId in chat.Products)
                if (tree.Nodes.TryGetValue(productId, out var product))
                    Products.Add(productId, product.Name);

            // TODO : fix managers for private chats
            foreach (var user in userManager.GetUsers())
                foreach (var (productId, role) in user.ProductsRoles)
                    if (Products.ContainsKey(productId) && role == Authentication.ProductRoleEnum.ProductManager && !Managers.ContainsKey(user.Id))
                        Managers.Add(user.Id, user.Name);
        }
    }
}
