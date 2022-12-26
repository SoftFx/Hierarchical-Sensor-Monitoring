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
    public static class ServerSettings
    {
        public static X509Certificate2 Certificate { get; private set; }
        
        public static KestrelConfig KestrelConfig { get; private set; }
        
        
        public static string Version { get; }

        public static string Name { get; }
        
        static ServerSettings()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            var version = assembly.Version;

            Name = assembly.Name;

            if (version is not null)
                Version = $"{version.Major}.{version.Minor}.{version.Build}";
        }
        
        public static void InitializeSettings(IConfigurationRoot configuration)
        {
            var certificate = configuration.GetSection("ServerCertificate").Get<CertificateConfig>();
            Certificate = GetCertificate(certificate);
            KestrelConfig = configuration.GetSection("Kestrel").Get<KestrelConfig>();
        }
        
        private static X509Certificate2 GetCertificate(CertificateConfig certificateConfig)
        {
            var folderPath = System.Diagnostics.Debugger.IsAttached
                ? @$"{Directory.GetCurrentDirectory()}\Config\"
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\app\Config"));

            return string.IsNullOrEmpty(certificateConfig.Key)
                ? new X509Certificate2($"{folderPath}{certificateConfig.Name}")
                : new X509Certificate2($"{folderPath}{certificateConfig.Name}", certificateConfig.Key);
        }
    }
}