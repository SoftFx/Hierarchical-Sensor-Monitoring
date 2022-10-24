using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
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

        internal string Name { get; }

        public NotificationSettings Notifications { get; }

        internal ConcurrentDictionary<Telegram.Bot.Types.ChatId, TelegramChat> Chats =>
            Notifications?.Telegram.Chats ?? new();


        internal bool AreNotificationsEnabled(BaseSensorModel sensor);

        internal bool WhetherSendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minStatus = Notifications.Telegram.MessagesMinStatus;

            return AreNotificationsEnabled(sensor) &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }
    }


    internal sealed class NotificatableComparator : IEqualityComparer<INotificatable>
    {
        public bool Equals(INotificatable x, INotificatable y) => x.Id == y.Id;

        public int GetHashCode([DisallowNull] INotificatable obj) => obj.Id.GetHashCode();
    }


    public static class NotificatableExtensions
    {
        public static void UpdateEntity(this INotificatable entity, IUserManager userManager, ITreeValuesCache cache)
        {
            if (entity is User user)
                userManager.UpdateUser(user);
            else if (entity is ProductModel product)
                cache.UpdateProduct(product);
        }

        internal static string BuildGreetings(this INotificatable entity) =>
            entity switch
            {
                User user => $"Hi, {user.UserName}. ",
                ProductModel => $"Hi. ",
                _ => string.Empty,
            };

        internal static string BuildSuccessfullResponse(this INotificatable entity) =>
            entity switch
            {
                User => "You are succesfully authorized.",
                ProductModel product => $"Product '{product.DisplayName}' is successfully added to group.",
                _ => string.Empty,
            };
    }
}
