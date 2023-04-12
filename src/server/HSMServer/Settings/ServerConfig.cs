using HSMCommon;
using HSMServer.Settings;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMCommon.Extensions;

namespace HSMServer.Model
{
    public class ServerConfig
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
        public static string ConfigPath => Path.Combine(Environment.CurrentDirectory, "Config");

        [JsonIgnore]
        public static string Version { get; }

        [JsonIgnore]
        public static string Name { get; }
        
        [JsonIgnore]
        public static string ExecutableDirectory { get; }


        public KestrelConfig Kestrel { get; }

        public ServerCertificateConfig ServerCertificate { get; }


        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();

            Name = assembly.Name;
            ExecutableDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            if (assembly.Version is not null)
                Version = assembly.GetVersion();

            if (!Directory.Exists(ConfigPath))
                FileManager.SafeCreateDirectory(ConfigPath);
        }

        public ServerConfig(IConfigurationRoot configuration)
        {
            _configuration = configuration;

            Kestrel = Register<KestrelConfig>(nameof(Kestrel));
            ServerCertificate = Register<ServerCertificateConfig>(nameof(ServerCertificate));

            ResaveSettings();
        }


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>() ?? new T();
        }

        private void ResaveSettings() =>
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(this, _options));
    }
}