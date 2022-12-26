using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using HSMServer.Certificates;
using HSMServer.Settings;
using Microsoft.Extensions.Configuration;

namespace HSMServer.Model
{
    public class ServerConfig
    {
        public static CertificateConfig CertificateConfig { get; private set; }
        
        public static KestrelConfig KestrelConfig { get; private set; }
        
        
        public static string Version { get; }

        public static string Name { get; }

        public ServerConfig(IConfigurationRoot configuration)
        {
            CertificateConfig = configuration.GetSection("ServerCertificate").Get<CertificateConfig>();
            KestrelConfig = configuration.GetSection("Kestrel").Get<KestrelConfig>();
        }
        
        static ServerConfig()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version;

            Name = assembly.Name;

            if (version is not null)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}