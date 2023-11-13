namespace HSMServer.ServerConfiguration
{
    public interface IServerConfig
    {
        TelegramConfig Telegram { get; }

        BackupDatabaseConfig BackupDatabase { get; }


        void ResaveSettings();
    }
}