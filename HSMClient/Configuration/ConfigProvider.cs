using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using HSMClient.Common.Logging;
using HSMCommon;

namespace HSMClient.Configuration
{
    public class ConfigProvider
    {
        #region Singleton

        private static volatile ConfigProvider _instance;
        private static readonly object _syncRoot = new object();

        // Multithread singleton
        public static ConfigProvider Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if(_instance == null)
                            _instance = new ConfigProvider();
                    }
                }

                return _instance;
            }
        }

        #endregion

        #region Private fields

        private readonly ConnectionInfo _connectionInfo;
        private const string _configFolderName = "Config";
        private const string _certificatesFolderName = "Certificates";
        private const string _configFileName = "config.xml";
        private string _certFileName;
        private readonly string _configFilePath;
        private readonly string _configFolderPath;
        private readonly string _certificatesFolderPath;
        private readonly object _configLock = new object();

        public string CertificatesFolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName,
            _certificatesFolderName);

        #endregion

        public ConnectionInfo ConnectionInfo
        {
            get { return _connectionInfo; }
        }
        public ConfigProvider()
        {
            _connectionInfo = new ConnectionInfo();
            _configFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName);
            _configFilePath = Path.Combine(_configFolderPath, _configFileName);
            _certificatesFolderPath = Path.Combine(_configFolderPath, _certificatesFolderName);
            if (!CheckFoldersFilesExistence())
            {
                CreateDefaultConfig();
            }

            ReadConnectionInfo();
        }

        private void ReplaceClientCertificateFile(X509Certificate2 newCertificate, string fileName)
        {
            string fullFileName = $"{fileName}.pfx";
            string templatePath = Path.Combine(Path.GetTempPath(), fullFileName);
            byte[] newCertBytes = newCertificate.Export(X509ContentType.Pkcs12);
            FileManager.SafeWriteBytes(templatePath, newCertBytes);
            string currentCertPath = GetCurrentCertificatePath();
            FileManager.SafeDelete(currentCertPath);
            FileManager.SafeCopy(templatePath, Path.Combine(CertificatesFolderPath, fullFileName));
            FileManager.SafeDelete(templatePath);
        }

        private string GetCurrentCertificatePath()
        {
            string[] files = Directory.GetFiles(CertificatesFolderPath, "*.pfx");
            if (files.Length < 1)
            {
                Logger.Error("No client certificate file!");
                throw new Exception("No client certificate file!");
            }

            return files[0];
        }

        private void CreateDefaultConfig()
        {
            if (!Directory.Exists(_configFolderPath))
            {
                FileManager.SafeCreateDirectory(_configFolderPath);
            }

            if (!Directory.Exists(_certificatesFolderPath))
            {
                FileManager.SafeCreateDirectory(_certificatesFolderPath);
            }
            

            if (!File.Exists(_configFilePath))
            {
                FileManager.SafeCreateFile(_configFilePath);
            }

            //TODO: save default config


        }

        private bool CheckFoldersFilesExistence()
        {
            if (!Directory.Exists(_configFolderPath))
                return false;

            if (!File.Exists(_configFilePath))
                return false;

            if (!Directory.Exists(_certificatesFolderPath))
                return false;

            return true;
        }
        private void ReadConnectionInfo()
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(_configFilePath);

                XmlNode connectionNode = document.SelectSingleNode("//config/connection");

                XmlAttribute addresAttr = connectionNode?.Attributes?["address"];
                _connectionInfo.Address = addresAttr?.Value;

                XmlAttribute portAttr = connectionNode?.Attributes?["port"];
                _connectionInfo.Port = portAttr?.Value;

                //_connectionInfo.ClientCertificate =
                //    CertificateReader.ReadCertificateFromPEMCertAndKey(CertFilePath, KeyFilePath);

                _connectionInfo.ClientCertificate = ReadClientCertificate();
            }
            catch (Exception e)
            {
                
            }
        }

        private X509Certificate2 ReadClientCertificate()
        {
            string certFolder = Path.Combine(_configFolderPath, _certificatesFolderName);

            if (!Directory.Exists(certFolder))
            {
                throw new Exception("Client certificate folder does not exist!");
            }

            string[] files = Directory.GetFiles(certFolder, "*.pfx");
            if (files.Length < 1)
            {
                throw new Exception("No client certificate provided!");
            }

            try
            {
                var type = X509Certificate2.GetCertContentType(files[0]);
                X509Certificate2 certificate = new X509Certificate2(files[0], "", X509KeyStorageFlags.DefaultKeySet);
                if (IsCertificateDefault(certificate))
                {
                    InstallCertificate(certificate);
                }
                return certificate;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to read certificate, error = {e}");
                return new X509Certificate2();
                //throw;
            }
        }

        private void ConfigInfoToXml(ConnectionInfo connectionInfo)
        {
            XmlDocument document = new XmlDocument();
            XmlElement rootElement = document.CreateElement("config");

            XmlElement connectionElement = document.CreateElement("connection");
            rootElement.AppendChild(connectionElement);

            XmlAttribute addressAttr = document.CreateAttribute("address");
            addressAttr.Value = connectionInfo.Address;
            connectionElement.Attributes.Append(addressAttr);

            XmlAttribute portAttr = document.CreateAttribute("port");
            portAttr.Value = connectionInfo.Port;
            connectionElement.Attributes.Append(portAttr);
        }

        public void UpdateClientCertificate(X509Certificate2 certificate, string fileName)
        {
            ReplaceClientCertificate(certificate);
            InstallCertificate(certificate);
            ReplaceClientCertificateFile(certificate, fileName);
        }

        private void ReplaceClientCertificate(X509Certificate2 certificate)
        {
            ConnectionInfo.ClientCertificate = certificate;
        }
        private void InstallCertificate(X509Certificate2 certificate)
        {
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);
            store.Close();
        }

        public bool IsCertificateDefault(X509Certificate2 certificate)
        {
            return certificate.Thumbprint.Equals(
                CommonConstants.DefaultClientCertificateThumbprint, StringComparison.OrdinalIgnoreCase);
        }
    }
}
