using System;
using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;
using HSMCommon;
using HSMServer.Settings;
using Microsoft.Extensions.Configuration;

namespace HSMServer.Model
{
    public class ServerConfig
    {
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


        public ServerCertificateConfig ServerCertificate { get; }

        public KestrelConfig Kestrel { get; }


        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version;

            Name = assembly.Name;

            if (version is not null)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";

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
            return _configuration.GetSection(sectionName).Get<T>();
        }
        
        private void ResaveSettings() => File.WriteAllText(_settingsPath, System.Text.Json.JsonSerializer.Serialize(this));
    }
}