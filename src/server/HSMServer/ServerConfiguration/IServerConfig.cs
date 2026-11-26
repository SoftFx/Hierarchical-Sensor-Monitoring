namespace HSMServer.ServerConfiguration
{
    public interface IServerConfig
    {
        KestrelConfig Kestrel { get; }

        TelegramConfig Telegram { get; }

        BackupDatabaseConfig BackupDatabase { get; }

        MonitoringOptions MonitoringOptions { get; }

        ServerCertificateConfig ServerCertificate { get; }

        AgentConfig Agent { get; }

        ApiTokensConfig ApiTokens { get; }

        CollectorSessionsConfig CollectorSessions { get; }

        HistorySealingConfig HistorySealing { get; }

        HistoryCompactionConfig HistoryCompaction { get; }

        TableSensorsConfig TableSensors { get; }

        LargeFileArchivesConfig LargeFileArchives { get; }
        LargeFilesConfig LargeFiles { get; }

        OidcConfig Oidc { get; }


        void ResaveSettings();
    }
}