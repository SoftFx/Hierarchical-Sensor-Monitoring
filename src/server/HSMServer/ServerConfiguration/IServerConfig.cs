namespace HSMServer.ServerConfiguration
{
    public interface IServerConfig
    {
        TelegramConfig Telegram { get; }

        void ResaveSettings();
    }
}