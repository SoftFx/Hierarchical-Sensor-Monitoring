using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using HSMCommon;
using HSMCommon.Certificates;
using HSMCommon.Constants;
using HSMServer.Core.DataLayer;
using NLog;

namespace HSMServer.Core.Configuration
{
    public static class CertificatesConfig
    {
        private const string CaCertificateFileName = "ca.crt";
        private const string CaKeyFileName = "ca.key.pem";
        private const string ConfigFolderName = "Config";
        private const string CertificatesFolderName = "Certificates";
        private const string CAFolderName = "CA";

        private static readonly string _configFileName = "config.xml";

        private static Logger _logger;
        private static bool _isFirstLaunch = false;

        private static string _configFilePath;
        private static string _serverCertName;
        private static string _caFolderPath;
        private static string _serverCertificatePath;

        private static X509Certificate2 _serverCertificate;
        private static X509Certificate2 _caCertificate;

        private static string ServerCertName
        {
            get
            {
                if (string.IsNullOrEmpty(_serverCertName) && !_isFirstLaunch)
                {
                    ReadConfig();
                }

                if (_isFirstLaunch)
                {
                    _serverCertName = CommonConstants.DefaultServerPfxCertificateName;
                }

                return _serverCertName;
            }
        }

        private static string CACertificatePath =>
            Path.Combine(_caFolderPath, CaCertificateFileName);

        public static IDatabaseAdapter DatabaseAdapter { get; private set; }

        public static X509Certificate2 ServerCertificate =>
            _serverCertificate ??= ReadServerCertificate();

        public static X509Certificate2 CACertificate =>
            _caCertificate ??= ReadCACertificate();

        public static string CAKeyFilePath { get; private set; }

        public static string CertificatesFolderPath { get; private set; }

        public static string ConfigFolderPath { get; private set; }


        public static void InitializeConfig()
        {
            _logger = LogManager.GetCurrentClassLogger();
            DatabaseAdapter = new DatabaseAdapter();

            InitializeIndependentConstants();

            if (!Directory.Exists(ConfigFolderPath))
            {
                FileManager.SafeCreateDirectory(ConfigFolderPath);
            }

            if (!File.Exists(_configFilePath))
            {
                _isFirstLaunch = true;

                CreateDefaultConfig();

                _serverCertificatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    CommonConstants.DefaultCertificatesFolderName,
                    CommonConstants.DefaultServerPfxCertificateName);

                string defaultCAPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    CommonConstants.DefaultCertificatesFolderName,
                    CommonConstants.DefaultCACrtCertificateName);
                X509Certificate2 cert = new X509Certificate2(defaultCAPath);
                CertificatesProcessor.AddCertificateToTrustedRootCA(cert);
            }

            InitializeDependentConstants();

            if (!Directory.Exists(_caFolderPath))
            {
                FileManager.SafeCreateDirectory(_caFolderPath);
                CreateCertificateAuthority();
            }

            _logger.Info("Config initialized, config file created/exists");

            if (_isFirstLaunch)
            {
                FileManager.SafeCopy(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                 CommonConstants.DefaultCertificatesFolderName,
                                 CommonConstants.DefaultClientCrtCertificateName),
                    Path.Combine(CertificatesFolderPath, CommonConstants.DefaultClientCrtCertificateName));

                _logger.Info("Added default client certificate to certificates folder");
            }
        }

        private static void InitializeIndependentConstants()
        {
            ConfigFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName);
            _configFilePath = Path.Combine(ConfigFolderPath, _configFileName);
            CertificatesFolderPath = Path.Combine(ConfigFolderPath, CertificatesFolderName);
            _caFolderPath = Path.Combine(CertificatesFolderPath, CAFolderName);
            CAKeyFilePath = Path.Combine(_caFolderPath, CaKeyFileName);
        }

        private static void InitializeDependentConstants() =>
            _serverCertificatePath = _isFirstLaunch
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                               CommonConstants.DefaultCertificatesFolderName,
                               CommonConstants.DefaultServerPfxCertificateName)
                : Path.Combine(CertificatesFolderPath, ServerCertName);

        private static void CreateDefaultConfig()
        {
            string defaultConfigContent = GetDefaultConfig();
            FileManager.SafeWriteToFile(_configFilePath, defaultConfigContent);
        }

        private static void CreateCertificateAuthority()
        {
            CertificateData data = new CertificateData
            {
                CommonName = "HSM CA",
                CountryName = RegionInfo.CurrentRegion.TwoLetterISORegionName,
                OrganizationName = "HSM"
            };

            X509Certificate2 caCertificate = CertificatesProcessor.CreateSelfSignedCertificate(data);
            CertificatesProcessor.ExportCrt(caCertificate, Path.Combine(_caFolderPath, CaCertificateFileName));
            CertificatesProcessor.ExportPEMPrivateKey(caCertificate, CAKeyFilePath);
            CertificatesProcessor.AddCertificateToTrustedRootCA(caCertificate);

            _logger.Info("CA created");
        }

        private static X509Certificate2 ReadServerCertificate()
        {
            if (!_isFirstLaunch)
            {
                var pwdParam =
                    DatabaseAdapter.GetConfigurationObject(ConfigurationConstants.ServerCertificatePassword);

                return pwdParam != null
                    ? new X509Certificate2(_serverCertificatePath, pwdParam.Value)
                    : new X509Certificate2(_serverCertificatePath);
            }

            string certOriginalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                CommonConstants.DefaultCertificatesFolderName, CommonConstants.DefaultServerPfxCertificateName);
            X509Certificate2 serverCert = new X509Certificate2(certOriginalPath);

            try
            {
                Task.Run(() => FileManager.SafeCopy(certOriginalPath,
                    Path.Combine(CertificatesFolderPath, CommonConstants.DefaultServerPfxCertificateName)));

                CertificatesProcessor.InstallCertificate(serverCert);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to copy default server certificate");
            }

            return serverCert;
        }

        private static X509Certificate2 ReadCACertificate() =>
            CertificatesProcessor.ReadCertificate(CACertificatePath, CAKeyFilePath);

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
                _serverCertName = certAttr.Value;
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

            StringBuilder sb = new StringBuilder(1 << 10);
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
        }
    }
}
