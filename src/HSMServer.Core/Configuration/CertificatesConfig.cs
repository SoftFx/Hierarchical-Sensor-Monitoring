using HSMCommon;
using HSMCommon.Certificates;
using HSMCommon.Constants;
using HSMCommon.Model;
using HSMDatabase.DatabaseInterface;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.DataLayer;
using NLog;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HSMServer.Core.Configuration
{
    public static class CertificatesConfig
    {
        #region Sync objects

        private static object _certificateSync = new object();

        #endregion

        #region Private fields

        private static ClientVersionModel _lastAvailableClientVersion;
        private static bool _isFirstLaunch = false;
        private static Logger _logger;
        private static string _configFolderPath;
        private static string _configFilePath;
        private static string _serverCertName;
        private static string _caFolderPath;
        private static string _certificatesFolderPath;
        private static string _serverCertificatePath;
        private static string _caKeyFilePath;
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

        private static string CACertificatePath => Path.Combine(_caFolderPath, _caCertificateFileName);
        private static X509Certificate2 _serverCertificate;
        private static X509Certificate2 _caCertificate;
        private static X509Certificate2 _caCertificateWithKey;
        private static IDatabaseAdapter _databaseAdapter;
        #endregion

        #region Public fields
        public static X509Certificate2 ServerCertificate => _serverCertificate ??= ReadServerCertificate();

        public static X509Certificate2 CACertificate => _caCertificate ??= ReadCACertificate();
        public static string CAKeyFilePath => _caKeyFilePath;
        public static string CertificatesFolderPath => _certificatesFolderPath;
        public static string ConfigFolderPath => _configFolderPath;

        #endregion


        public static void InitializeConfig()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _databaseAdapter = new DatabaseAdapter();

            InitializeIndependentConstants();

            if (!Directory.Exists(_configFolderPath))
            {
                FileManager.SafeCreateDirectory(_configFolderPath);
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
                FileManager.SafeCopy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CommonConstants.DefaultCertificatesFolderName,
                    CommonConstants.DefaultClientCrtCertificateName), Path.Combine(_certificatesFolderPath,
                    CommonConstants.DefaultClientCrtCertificateName));

                _logger.Info("Added default client certificate to certificates folder");
            }
        }

        private static void InitializeIndependentConstants()
        {
            _configFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName);
            _configFilePath = Path.Combine(_configFolderPath, _configFileName);
            _certificatesFolderPath = Path.Combine(_configFolderPath, _certificatesFolderName);
            _caFolderPath = Path.Combine(_certificatesFolderPath, _CAFolderName);
            _caKeyFilePath = Path.Combine(_caFolderPath, _caKeyFileName);
        }

        private static void InitializeDependentConstants()
        {
            if (_isFirstLaunch)
            {
                _serverCertificatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    CommonConstants.DefaultCertificatesFolderName,
                    CommonConstants.DefaultServerPfxCertificateName);
            }
            else
            {
                _serverCertificatePath = Path.Combine(_certificatesFolderPath, ServerCertName);
            }
        }

        private static void CreateDefaultConfig()
        {
            string defaultConfigContent = GetDefaultConfig();
            FileManager.SafeWriteToNewFile(_configFilePath, defaultConfigContent);

        }
        private static void CreateCertificateAuthority()
        {
            CertificateData data = new CertificateData();
            data.CommonName = "HSM CA";
            data.CountryName = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            data.OrganizationName = "HSM";
            X509Certificate2 caCertificate = CertificatesProcessor.CreateSelfSignedCertificate(data);
            CertificatesProcessor.ExportCrt(caCertificate, Path.Combine(_caFolderPath, _caCertificateFileName));
            CertificatesProcessor.ExportPEMPrivateKey(caCertificate, _caKeyFilePath);
            CertificatesProcessor.AddCertificateToTrustedRootCA(caCertificate);

            _logger.Info("CA created");
        }

        private static X509Certificate2 ReadServerCertificate()
        {
            //return CertificateReader.ReadCertificateFromPEMCertAndKey(ServerCertPath, ServerKeyPath);
            if (!_isFirstLaunch)
            {
                //var pwdParam = _databaseAdapter.GetConfigurationObjectOld(ConfigurationConstants.ServerCertificatePassword);
                var pwdParam =
                    _databaseAdapter.GetConfigurationObject(ConfigurationConstants.ServerCertificatePassword);
                if (pwdParam != null)
                {
                    return new X509Certificate2(_serverCertificatePath,pwdParam.Value);
                }
                return new X509Certificate2(_serverCertificatePath);
            }

            string certOriginalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                CommonConstants.DefaultCertificatesFolderName, CommonConstants.DefaultServerPfxCertificateName);

            X509Certificate2 serverCert = new X509Certificate2(certOriginalPath);
            try
            {
                Task.Run(() => FileManager.SafeCopy(certOriginalPath, Path.Combine(_certificatesFolderPath,
                    CommonConstants.DefaultServerPfxCertificateName)));
                CertificatesProcessor.InstallCertificate(serverCert);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to copy default server certificate");                
            }
            
            return serverCert;
        }

        private static X509Certificate2 ReadCACertificate()
        {
            return CertificatesProcessor.ReadCertificate(CACertificatePath, CAKeyFilePath);
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

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
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
