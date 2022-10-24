using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;

namespace HSMServer.Notifications
{
    internal static class NotificatableExtensions
    {
        internal static void UpdateEntity(this INotificatable entity, IUserManager userManager, ITreeValuesCache cache)
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
