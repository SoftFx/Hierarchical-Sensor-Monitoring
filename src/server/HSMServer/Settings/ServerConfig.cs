using System;
using System.IO;
using System.Reflection;
using HSMServer.Settings;
using Microsoft.Extensions.Configuration;

namespace HSMServer.Model
{
    public class ServerConfig
    {
        private readonly IConfigurationRoot _configuration;
        
        
        public static string ConfigPath { get; } = Path.Combine(Environment.CurrentDirectory, "Config");
        
        public static string Version { get; }

        public static string Name { get; }

        
        public ServerCertificateConfig ServerCertificate { get; set; }

        public KestrelConfig Kestrel { get; set; }


        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version;

            Name = assembly.Name;

            if (version is not null)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public ServerConfig(IConfigurationRoot configuration)
        {
            _configuration = configuration;

            Kestrel = Register<KestrelConfig>(nameof(Kestrel));
            ServerCertificate = Register<ServerCertificateConfig>(nameof(ServerCertificate));
        }


        private T Register<T>(string sectionName) where T : class, new()
        {
            return _configuration.GetSection(sectionName).Get<T>();
        }
    }
}