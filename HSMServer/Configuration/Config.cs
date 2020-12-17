using System;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using HSMCommon;
using HSMCommon.Certificates;
using NLog;

namespace HSMServer.Configuration
{
    public static class Config
    {
        #region Sync objects

        private static object _certificateSync = new object();

        #endregion

        #region Private fields
        private static Logger _logger;
        private static string _configFolderPath;
        private static string _configFilePath;
        private static string _serverCertName;
        private static string _caFolderPath;
        private static string _certificatesFolderPath;
        private static string _serverCertificatePath;
        private static string _caKeyFilePath;
        private static int _gRPCPort;
        private static int _sensorsPort;
        private static string _configFileName = "config.xml";
        private const string _caCertificateFileName = "ca.crt";
        private const string _caKeyFileName = "ca.key.pem";
        private const string _configFolderName = "Config";
        private const string _certificatesFolderName = "Certificates";
        private const string _CAFolderName = "CA";
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

        private static string CACertificatePath => Path.Combine(_caFolderPath, _caCertificateFileName);
        private static X509Certificate2 _serverCertificate;
        private static X509Certificate2 _caCertificate;
        #endregion

        #region Public fields
        public const int GrpcPort = 5015;
        public const int SensorsPort = 44330;
        public static X509Certificate2 ServerCertificate => _serverCertificate ??= ReadServerCertificate();

        public static X509Certificate2 CACertificate => _caCertificate ??= ReadCACertificate();
        public static string CAKeyFilePath => _caKeyFilePath;
        public static string CertificatesFolderPath => _certificatesFolderPath;
        public static string ConfigFolderPath => _configFolderPath;

        #endregion


        public static void InitializeConfig()
        {
            _logger = LogManager.GetCurrentClassLogger();

            InitializeConstants();

            if (!Directory.Exists(_configFolderPath))
            {
                FileManager.SafeCreateDirectory(_configFolderPath);
            }

            if (!File.Exists(_configFilePath))
            {
                FileManager.SafeCreateFile(_configFilePath);
            }

            if (!Directory.Exists(_caFolderPath))
            {
                FileManager.SafeCreateDirectory(_caFolderPath);
                CreateCertificateAuthority();
            }

            _logger.Info("Config initialized, config file created/exists");
        }

        private static void InitializeConstants()
        {
            _configFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName);
            _configFilePath = Path.Combine(_configFolderPath, _configFileName);
            _certificatesFolderPath = Path.Combine(_configFolderPath, _certificatesFolderName);
            _caFolderPath = Path.Combine(_certificatesFolderPath, _CAFolderName);
            _caKeyFilePath = Path.Combine(_caFolderPath, _caKeyFileName);
            _serverCertificatePath = Path.Combine(_certificatesFolderPath, ServerCertName);
        }
        private static void CreateCertificateAuthority()
        {
            CertificateData data = new CertificateData();
            data.CommonName = "HSM CA";
            data.CountryName = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            data.OrganizationName = "HSM";
            X509Certificate2 caCertificate = CertificatesProcessor.CreateSelfSignedCertificate(data, true);
            CertificatesProcessor.ExportCrt(caCertificate, Path.Combine(_caFolderPath, _caCertificateFileName));
            CertificatesProcessor.ExportPEMPrivateKey(caCertificate, _caKeyFilePath);
            CertificatesProcessor.AddCertificateToTrustedRootCA(caCertificate);

            _logger.Info("CA created");
        }

        private static X509Certificate2 ReadServerCertificate()
        {
            //return CertificateReader.ReadCertificateFromPEMCertAndKey(ServerCertPath, ServerKeyPath);
            X509Certificate2 certificate =  new X509Certificate2(_serverCertificatePath);

            return certificate;
        }

        private static X509Certificate2 ReadCACertificate()
        {
            return new X509Certificate2(CACertificatePath, "",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
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

            //XmlNode endpointsConfigNode = serverConfigNode.SelectSingleNode("endpointsConfiguration");

            //var gRPCPortAttr = endpointsConfigNode?.Attributes?["gRPCPort"];
            //if (gRPCPortAttr != null)
            //{
            //    _gRPCPort = int.Parse(gRPCPortAttr.Value);
            //}

            //var sensorsPortAttr = endpointsConfigNode?.Attributes?["sensorsPort"];
            //if (sensorsPortAttr != null)
            //{
            //    _sensorsPort = int.Parse(sensorsPortAttr.Value);
            //}
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

        private static string GetDefaultConfig()
        {
            XmlDocument document = new XmlDocument();
            XmlElement rootElement = document.CreateElement("config");
            document.AppendChild(rootElement);

            XmlElement configElement = document.CreateElement("serverConfiguration");
            rootElement.AppendChild(configElement);

            XmlAttribute certificateAttr = document.CreateAttribute("certificate");
            certificateAttr.Value = CommonConstants.DefaultServerPfxCertificateName;
            configElement.Attributes.Append(certificateAttr);

            return string.Empty;
        }

        public static void InstallCertificate(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            try
            {
                
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
                store.Close();
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to install certificate with thumbprint = {certificate.Thumbprint}");
            }
            finally
            {
                store.Close();
            }
            
        }

        public static void Dispose()
        {
            //SaveConfigFile();
            _logger = null;
        }
    }
}
