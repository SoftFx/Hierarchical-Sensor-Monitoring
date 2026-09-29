using HSMCommon;
using HSMServer.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration
{
    public class ServerConfig : IServerConfig
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
        };

        private readonly string _settingsPath = Path.Combine(ConfigPath, ConfigName);

        private readonly IConfigurationRoot _configuration;


#if RELEASE
        public const string ConfigName = "appsettings.json";
#else
        public const string ConfigName = "appsettings.Development.json";
#endif

        [JsonIgnore]
        public static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");

        [JsonIgnore]
        public static string ExecutableDirectory { get; }


        [JsonIgnore]
        public static string Version { get; }

        [JsonIgnore]
        public static string Name { get; }

        [JsonIgnore]
        public static string Company { get; }


        public ServerCertificateConfig ServerCertificate { get; }

        public BackupDatabaseConfig BackupDatabase { get; }

        public TelegramConfig Telegram { get; }

        public KestrelConfig Kestrel { get; }

        [JsonIgnore]
        public string TrustedProxiesIgnoredInSettingsFile { get; }

        // The settings file held the pre-rename kill switch "ApiTokens.Enabled": false and
        // no "Disabled" key. It is ignored (it was also the old persisted default, the two
        // cannot be told apart), so tokens are ON after the upgrade — Program logs a warning.
        [JsonIgnore]
        public bool LegacyApiTokensKillSwitchIgnored { get; }

        public MonitoringOptions MonitoringOptions { get; }

        public AgentConfig Agent { get; }

        public ApiTokensConfig ApiTokens { get; }


        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();

            Name = assembly.Name;
            ExecutableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            Version = assembly.GetVersion().RemoveTailZeroes();
            Company = GetCompany();
            if (!Directory.Exists(ConfigPath))
                FileManager.SafeCreateDirectory(ConfigPath);
        }


        private static string GetCompany()
        {
            return Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyCompanyAttribute>()
                ?.Company ?? "Unknown";
        }


        public ServerConfig(IConfigurationRoot configuration)
        {
            _configuration = configuration;

            ServerCertificate = Register<ServerCertificateConfig>(nameof(ServerCertificate));
            BackupDatabase = Register<BackupDatabaseConfig>(nameof(BackupDatabase));
            Telegram = Register<TelegramConfig>(nameof(Telegram));
            Kestrel = Register<KestrelConfig>(nameof(Kestrel));
            MonitoringOptions = Register<MonitoringOptions>(nameof(MonitoringOptions));
            Agent = Register<AgentConfig>(nameof(Agent));
            ApiTokens = Register<ApiTokensConfig>(nameof(ApiTokens));

            // Deployment-owned, environment only (#1427): a copy in the settings file is ignored.
            Kestrel.TrustedProxies = KestrelConfig.ReadTrustedProxies(new ConfigurationBuilder().AddEnvironmentVariables().Build());
            TrustedProxiesIgnoredInSettingsFile = KestrelConfig.FindTrustedProxiesInSettingsFile(configuration);

            var apiTokensSection = configuration.GetSection(nameof(ApiTokens));
            LegacyApiTokensKillSwitchIgnored = apiTokensSection["Enabled"] is { } legacy
                && string.Equals(legacy.Trim(), "false", StringComparison.OrdinalIgnoreCase)
                && apiTokensSection[nameof(ApiTokensConfig.Disabled)] is null;

            // Startup validation with actionable errors (initiative, section
            // "Configuration"). Throws before the server starts serving.
            ApiTokens.Validate();
            Kestrel.Validate();

            ResaveSettings();

            // The API-token kill switch is config-file only (no UI toggle). Follow hand
            // edits of the running file so it applies without a restart — and so a later
            // ResaveSettings (any settings save) cannot write the stale value back.
            ChangeToken.OnChange(_configuration.GetReloadToken, SyncApiTokensKillSwitch);
        }

        private void SyncApiTokensKillSwitch()
        {
            try
            {
                // No section at all means the file is missing or empty mid-save (editors
                // and config tools delete+rename): keep the current state rather than
                // failing open, which a settings save in that window would persist.
                var section = _configuration.GetSection(nameof(ApiTokens));
                if (!section.Exists())
                    return;

                ApiTokens.Disabled = section.GetValue<bool?>(nameof(ApiTokensConfig.Disabled)) ?? false;
            }
            catch (InvalidOperationException)
            {
                // A malformed value in a half-written file: keep the current state; the
                // next change notification re-reads it.
            }
        }

        public void ResaveSettings() => File.WriteAllText(_settingsPath, JsonSerializer.Serialize(this, _options));


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>() ?? new T();
        }
    }
}