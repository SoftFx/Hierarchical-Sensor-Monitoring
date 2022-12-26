using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace HSMServer.Model
{
    public static class ServerSettings
    {
        public static X509Certificate2 Certificate { get; private set; }

        
        public static int SensorPort { get; private set; }

        public static int SitePort { get; private set; }
        
        
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
            var certificate = ((configuration.GetSection("Certificate:Name").Value), (configuration.GetSection("Certificate:Key").Value));
            int.TryParse(configuration.GetSection("SensorPort").Value, out var sensorPort);
            int.TryParse(configuration.GetSection("SitePort").Value, out var sitePort);

            SensorPort = sensorPort;
            SitePort = sitePort;

            Certificate = GetCertificate(certificate);
        }
        
        private static X509Certificate2 GetCertificate((string, string) certificate)
        {
            var folderPath = System.Diagnostics.Debugger.IsAttached
                ? @$"{Directory.GetCurrentDirectory()}\Config\"
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\app\Config"));

            return string.IsNullOrEmpty(certificate.Item2)
                ? new X509Certificate2($"{folderPath}{certificate.Item1}")
                : new X509Certificate2($"{folderPath}{certificate.Item1}", certificate.Item2);
        }
    }
}