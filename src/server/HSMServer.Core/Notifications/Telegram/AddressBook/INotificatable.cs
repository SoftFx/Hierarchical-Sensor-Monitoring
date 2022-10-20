using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Core.Notifications
{
    public interface INotificatable
    {
        internal string Id { get; }


        public NotificationSettings NotificationSettings
        {
            get
            {
                if (this is User user)
                    return user.Notifications;
                else if (this is ProductModel product)
                    return product.Notifications;

                return null;
            }
        }

        internal ConcurrentDictionary<Telegram.Bot.Types.ChatId, TelegramChat> Chats =>
            NotificationSettings?.Telegram.Chats ?? new();

        internal string BuildStartCommandGreetings()
        {
            if (this is User user)
                return $"Hi, {user.UserName}. ";
            else if (this is ProductModel)
                return $"Hi. ";

            return string.Empty;
        }

        internal string BuildStartCommandSuccessfullResponse()
        {
            if (this is User)
                return "You are succesfully authorized.";
            else if (this is ProductModel product)
                return $"Product '{product.DisplayName}' is successfully added to group.";

            return string.Empty;
        }
    }


    internal sealed class NotificatableComparator : IEqualityComparer<INotificatable>
    {
        public bool Equals(INotificatable x, INotificatable y) => x.Id == y.Id;

        public int GetHashCode([DisallowNull] INotificatable obj) => obj.GetHashCode();
    }
}
