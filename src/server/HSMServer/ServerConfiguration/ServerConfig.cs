using HSMCommon;
using HSMCommon.Extensions;
using HSMServer.Extensions;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration
{
    public class ServerConfig : IServerConfig
    {
        private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

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


        public ServerCertificateConfig ServerCertificate { get; }

        public KestrelConfig Kestrel { get; }

        public TelegramConfig Telegram { get; }


        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();

            Name = assembly.Name;
            ExecutableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            Version = assembly.GetVersion().RemoveTailZeroes();

            if (!Directory.Exists(ConfigPath))
                FileManager.SafeCreateDirectory(ConfigPath);
        }

        public ServerConfig(IConfigurationRoot configuration)
        {
            _configuration = configuration;

            ServerCertificate = Register<ServerCertificateConfig>(nameof(ServerCertificate));
            Telegram = Register<TelegramConfig>(nameof(Telegram));
            Kestrel = Register<KestrelConfig>(nameof(Kestrel));

            ResaveSettings();
        }


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>() ?? new T();
        }

        private void ResaveSettings() => File.WriteAllText(_settingsPath, JsonSerializer.Serialize(this, _options));
    }
}