namespace HSMServer.ServerConfiguration
{
    public interface IServerConfig
    {
        KestrelConfig Kestrel { get; }

        TelegramConfig Telegram { get; }

        BackupDatabaseConfig BackupDatabase { get; }

        MonitoringOptions MonitoringOptions { get; }

        ServerCertificateConfig ServerCertificate { get; }


        void ResaveSettings();
    }
}