using System;
using System.IO;
using System.Reflection;
using HSMCommon;
using HSMServer.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HSMServer.Model
{
    public class ServerConfig
    {
        private const string DefaultSettingsValues = """
        {
            "Kestrel": {
                "SensorPort": "44330",
                "SitePort": "44333"
            },
            "ServerCertificate": {
                "Name": "default.server.pfx",
                "Key": ""
            }
        }
        """;

        private readonly IConfigurationRoot _configuration;


        public static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");

        public static string Version { get; }

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

        public ServerConfig(IConfigurationRoot configuration, IWebHostEnvironment webHostEnvironment)
        {
            _configuration = configuration;

            CreateIfNotExistsSettings(webHostEnvironment);
    
            Kestrel = Register<KestrelConfig>(nameof(Kestrel));
            ServerCertificate = Register<ServerCertificateConfig>(nameof(ServerCertificate));
        }


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>();
        }

        private void CreateIfNotExistsSettings(IWebHostEnvironment webHostEnvironment)
        {
            string file = Path.Combine(ConfigPath, "appsettings" + (webHostEnvironment.IsDevelopment() ? ".Development" : string.Empty) + ".json");

            if (!File.Exists(file))
                FileManager.SafeWriteToFile(file, DefaultSettingsValues);
            
            _configuration.Reload();
        }
    }
}