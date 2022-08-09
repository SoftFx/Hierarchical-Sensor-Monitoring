namespace HSMServer.Core.Notifications
{
    public interface INotificationsCenter
    {
        TelegramBot TelegramBot { get; }
    }
}
