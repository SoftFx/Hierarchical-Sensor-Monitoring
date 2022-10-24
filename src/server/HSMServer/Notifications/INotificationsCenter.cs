namespace HSMServer.Notifications
{
    public interface INotificationsCenter
    {
        TelegramBot TelegramBot { get; }
    }
}