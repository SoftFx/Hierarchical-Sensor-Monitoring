using HSMCommon;
using HSMServer.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NLog;
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

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _resaveLock = new();

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

                SetApiTokensKillSwitch(section.GetValue<bool?>(nameof(ApiTokensConfig.Disabled)) ?? false, "configuration reload");
            }
            catch (InvalidOperationException)
            {
                // A malformed value in a half-written file: keep the current state; the
                // next change notification re-reads it.
            }
        }

        // The file watcher can miss a hand edit (bind mounts, Docker Desktop, network
        // file systems). Before overwriting the file, adopt the kill switch it holds so a
        // settings save can never silently revert an emergency edit. Absent or malformed
        // values keep the current state, as on reload.
        private void SyncApiTokensKillSwitchFromFile()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return;

                using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(nameof(ApiTokens), out var section)
                    && section.ValueKind == JsonValueKind.Object
                    && section.TryGetProperty(nameof(ApiTokensConfig.Disabled), out var disabled))
                {
                    // The configuration binder also accepts "true"/"false" strings.
                    bool? value = disabled.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String when bool.TryParse(disabled.GetString()?.Trim(), out var parsed) => parsed,
                        _ => null,
                    };

                    if (value is { } known)
                        SetApiTokensKillSwitch(known, "settings file before save");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        private void SetApiTokensKillSwitch(bool disabled, string source)
        {
            if (ApiTokens.Disabled == disabled)
                return;

            ApiTokens.Disabled = disabled;
            _logger.Warn($"ApiTokens.Disabled changed to {disabled} ({source}): API tokens are now {(disabled ? "OFF" : "ON")}.");
        }

        public void ResaveSettings()
        {
            lock (_resaveLock)
            {
                SyncApiTokensKillSwitchFromFile();
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(this, _options));
            }
        }


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>() ?? new T();
        }
    }
}