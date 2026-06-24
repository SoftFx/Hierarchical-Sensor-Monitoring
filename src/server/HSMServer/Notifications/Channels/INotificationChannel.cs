using HSMServer.Core.Managers;
using HSMServer.Core.Notifications;
using System.Threading.Tasks;

namespace HSMServer.Notifications.Channels
{
    public interface INotificationChannel
    {
        NotificationKind Kind { get; }

        Task DeliverAsync(AlertMessage message);

        Task FlushAsync();
    }
}
