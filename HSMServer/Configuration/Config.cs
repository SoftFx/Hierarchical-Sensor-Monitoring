using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using HSMCommon;
using NLog;

namespace HSMServer.Configuration
{
    public static class Config
    {
        #region Sync objects

        private static object _certificateSync = new object();

        #endregion

        #region Private fields

        public const string ConfigFolderName = "Config";
        public const string CertificatesFolderName = "Certificates";
        private static Logger _logger;
        private static string _configFilePath;
        private static string _serverCertName;
        private static int _gRPCPort;
        private static int _sensorsPort;
        private static string _configFileName = "config.xml";
        private static string ServerCertName
        {
            get
            {
                if (string.IsNullOrEmpty(_serverCertName))
                {
                    ReadConfig();
                }

                return _serverCertName;
            }
        }
        private static string ServerCertificatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            ConfigFolderName, CertificatesFolderName, ServerCertName);

        private static X509Certificate2 _serverCertificate;

        #endregion

        #region Public fields
        public static int GrpcPort
        {
            get
            {
                if (_gRPCPort == 0)
                {
                    ReadConfig();
                }

                return _gRPCPort;
            }
        }

        public static int SensorsPort
        {
            get
            {
                if (_sensorsPort == 0)
                {
                    ReadConfig();
                }

                return _sensorsPort;
            }
        }
        public static X509Certificate2 ServerCertificate
        {
            get
            {
                lock (_certificateSync)
                {
                    _serverCertificate ??= ReadServerCertificate();
                }

                return _serverCertificate;
            }
        }

        public static string ConfigFolderPath;

        public static string CertificatesFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName,
            CertificatesFolderName);
        #endregion


        public static void InitializeConfig()
        {
            _logger = LogManager.GetCurrentClassLogger();

            ConfigFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName);
            if (!Directory.Exists(ConfigFolderPath))
            {
                FileManager.SafeCreateDirectory(ConfigFolderPath);
            }

            _configFilePath = Path.Combine(ConfigFolderPath, _configFileName);
            if (!File.Exists(_configFilePath))
            {
                FileManager.SafeCreateFile(_configFilePath);
            }
            _logger.Info("Config initialized, config file created/exists");
        }

        private static X509Certificate2 ReadServerCertificate()
        {
            //return CertificateReader.ReadCertificateFromPEMCertAndKey(ServerCertPath, ServerKeyPath);
            X509Certificate2 certificate =  new X509Certificate2(ServerCertificatePath);

            return certificate;
        }

        private static void ReadConfig()
        {
            try
            {
                LoadConfig(_configFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load config");
            }
        }

        private static void LoadConfig(string configFilePath)
        {
            XmlDocument document = new XmlDocument();
            document.Load(configFilePath);

            ParseXmlConfig(document);
        }

        private static void ParseXmlConfig(XmlDocument document)
        {
            XmlNode serverConfigNode = document.SelectSingleNode("config/serverConfiguration");
            ParseServerConfigurationNode(serverConfigNode);
        }

        private static void ParseServerConfigurationNode(XmlNode serverConfigNode)
        {
            var certAttr = serverConfigNode.Attributes?["certificate"];
            if (certAttr != null)
            {
                _serverCertName = certAttr.Value;
            }

            XmlNode endpointsConfigNode = serverConfigNode.SelectSingleNode("endpointsConfiguration");

            var gRPCPortAttr = endpointsConfigNode?.Attributes?["gRPCPort"];
            if (gRPCPortAttr != null)
            {
                _gRPCPort = int.Parse(gRPCPortAttr.Value);
            }

            var sensorsPortAttr = endpointsConfigNode?.Attributes?["sensorsPort"];
            if (sensorsPortAttr != null)
            {
                _sensorsPort = int.Parse(sensorsPortAttr.Value);
            }
        }

        private static string ConfigToXml()
        {
            XmlDocument document = new XmlDocument();
            XmlElement rootElement = document.CreateElement("machines");
            document.AppendChild(rootElement);

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
        }

        public static void Dispose()
        {
            //SaveConfigFile();
            _logger = null;
        }
    }
}
