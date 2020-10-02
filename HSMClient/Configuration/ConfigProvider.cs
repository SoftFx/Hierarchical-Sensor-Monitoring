using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using HSMCommon;
using MAMSClient.Configuration;

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

        private List<MachineInfo> _machineInfos;
        private ConnectionInfo _connectionInfo;
        private CertificateInfo _certificateInfo;
        private const string _configFolderName = "Config";
        private const string _certificatesFolderName = "Certificates";
        private const string _configFileName = "config.xml";
        private readonly string _configFilePath;
        private readonly string _configFolderPath;
        private readonly object _configLock = new object();

        public string CertificatesFolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName,
            _certificatesFolderName);

        #endregion

        public List<MachineInfo> MachineInfos 
        {
            get
            {
                if (_machineInfos == null)
                {
                    lock (_configLock)
                    {
                        _machineInfos = ReadConfig();
                    }
                }

                return _machineInfos;
            }
        }

        public ConnectionInfo ConnectionInfo
        {
            get { return _connectionInfo; }
        }
        public ConfigProvider()
        {
            _configFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName);
            if (!Directory.Exists(_configFolderPath))
            {
                FileManager.SafeCreateDirectory(_configFolderPath);
            }

            _configFilePath = Path.Combine(_configFolderPath, _configFileName);
            if (!File.Exists(_configFilePath))
            {
                FileManager.SafeCreateFile(_configFilePath);
            }

            _certificateInfo = new CertificateInfo();
            ReadConnectionInfo();
        }

        private void ReadConnectionInfo()
        {
            try
            {
                UserInfo userInfo = new UserInfo();
                XmlDocument document = new XmlDocument();
                document.Load(_configFilePath);

                XmlNode userNode = document.SelectSingleNode("//config/credentials");

                XmlAttribute loginAttr = userNode?.Attributes?["login"];
                userInfo.Login = loginAttr?.Value;

                XmlAttribute passwordAttr = userNode?.Attributes?["password"];
                userInfo.Password = passwordAttr?.Value;

                _connectionInfo = new ConnectionInfo();
                _connectionInfo.UserInfo = userInfo;

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

            string[] files = Directory.GetFiles(certFolder, "*.crt");
            if (files.Length < 1)
            {
                throw new Exception("No client certificate provided!");
            }

            try
            {
                X509Certificate2 certificate = new X509Certificate2(files[0]);
                return certificate;
            }
            catch (Exception e)
            {
                return new X509Certificate2();
                //throw;
            }
        }

        private List<MachineInfo> ReadConfig()
        {
            List<MachineInfo> result = LoadConfig(_configFilePath);

            return result;
        }

        private List<MachineInfo> LoadConfig(string configFilePath)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(configFilePath);

                return ParseXmlConfig(document);
            }
            catch (Exception ex)
            {
                //TODO: log exception
                return new List<MachineInfo>();
            }
        }

        private List<MachineInfo> ParseXmlConfig(XmlDocument document)
        {
            XmlNodeList nodes = document.SelectNodes("//config/machines/machine");

            if (nodes == null)
                return new List<MachineInfo>();

            List<MachineInfo> result = new List<MachineInfo>();
            //Encryptor encryptor = new Encryptor(Environment.MachineName);

            foreach (XmlNode node in nodes)
            {
                if(!node.HasChildNodes || node.ChildNodes.Count < 1)
                    continue;

                MachineInfo info = new MachineInfo();
                XmlAttribute nameAttr = node.Attributes?["name"];
                if (nameAttr != null)
                    info.Name = nameAttr.Value;

                foreach (XmlNode childNode in node.ChildNodes)
                {
                    switch (childNode.Name)
                    {
                        case "sensors":
                            info.Sensors = ParseSensorsConfig(childNode, info.Name);
                            break;
                        case "tts":
                            info.TTSMonitoringInfo = ParseTTSMonitoringConfig(childNode);
                            break;
                        case "aggr":
                            info.AggrMonitoringInfo = ParseAggrMonitoringConfig(childNode);
                            break;
                    }
                }

                if (info.Sensors?.Any() == true || info.AggrMonitoringInfo != null || info.TTSMonitoringInfo != null)
                {
                    result.Add(info);
                }
                
            }

            return result;
        }

        private List<SensorMonitoringInfo> ParseSensorsConfig(XmlNode jobNode, string machineName)
        {
            List<SensorMonitoringInfo> result = new List<SensorMonitoringInfo>();

            foreach (XmlNode node in jobNode.ChildNodes)
            {
                SensorMonitoringInfo sensorInfo = new SensorMonitoringInfo();

                XmlAttribute nameAttr = node.Attributes?["name"];
                if (nameAttr != null)
                    sensorInfo.Name = nameAttr.Value;

                XmlAttribute warningAttr = node.Attributes?["warning"];
                if(warningAttr != null)
                    sensorInfo.WarningPeriod = new TimeSpan(long.Parse(warningAttr.Value));

                XmlAttribute errorAttr = node.Attributes?["error"];
                if(errorAttr != null)
                    sensorInfo.ErrorPeriod = new TimeSpan(long.Parse(errorAttr.Value));

                XmlAttribute updateAttr = node.Attributes?["update"];
                if(updateAttr != null)
                    sensorInfo.UpdatePeriod = new TimeSpan(long.Parse(updateAttr.Value));

                sensorInfo.MachineName = machineName;

                if (!string.IsNullOrEmpty(sensorInfo.Name) && sensorInfo.UpdatePeriod.Ticks != 0)
                {
                    result.Add(sensorInfo);
                }
            }

            return result;
        }

        private AggrMonitoringInfo ParseAggrMonitoringConfig(XmlNode aggrNode)
        {
            AggrMonitoringInfo result = new AggrMonitoringInfo();



            return result;
        }

        private TTSMonitoringInfo ParseTTSMonitoringConfig(XmlNode ttsNode)
        {
            TTSMonitoringInfo result = new TTSMonitoringInfo();



            return result;
        }

        public void SaveConfig()
        {
            string content = ConfigToXml();

            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _configFolderName, _configFileName);

            try
            {
                FileManager.SafeDelete(file);
            }
            catch (Exception ex)
            {
                //_logger.Error(ex, "Error deleting current connections file");
                throw;
            }

            try
            {
                FileManager.SafeWriteText(file, content);
                //_logger.Info("Config file saved successfully");
            }
            catch (Exception ex)
            {
                //_logger.Error(ex, "Failed to save config file!");
                throw;
            }
        }

        private string ConfigToXml()
        {
            XmlDocument document = new XmlDocument();

            XmlElement rootElement = document.CreateElement("config");
            document.AppendChild(rootElement);

            ConnectionInfoToXml(document, _connectionInfo);

            XmlElement machinesElement = document.CreateElement("machines");
            rootElement.AppendChild(machinesElement);

            //Encryptor encryptor = new Encryptor(Environment.MachineName);

            foreach (var machineInfo in MachineInfos)
            {
                XmlElement machineElement = document.CreateElement("machine");
                machinesElement.AppendChild(machineElement);


                SensorsToXml(document, machineElement,  machineInfo.Sensors);
                //TODO: aggr and TTS to xml
            }

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
        }

        private void ConnectionInfoToXml(XmlDocument document, ConnectionInfo connectionInfo)
        {
            //Encryptor encryptor = new Encryptor(Environment.MachineName);
            XmlElement credElement = document.CreateElement("credentials");
            document.AppendChild(credElement);

            XmlAttribute loginAttr = document.CreateAttribute("login");
            //loginAttr.Value = encryptor.Encrypt(connectionInfo.UserInfo.Login);
            loginAttr.Value = connectionInfo.UserInfo.Login;
            credElement.Attributes.Append(loginAttr);

            XmlAttribute passwordAttr = document.CreateAttribute("password");
            //passwordAttr.Value = encryptor.Encrypt(connectionInfo.UserInfo.Password);
            passwordAttr.Value = connectionInfo.UserInfo.Password;
            credElement.Attributes.Append(passwordAttr);

            XmlElement connectionElement = document.CreateElement("connection");
            document.AppendChild(connectionElement);

            XmlAttribute addressAttr = document.CreateAttribute("address");
            addressAttr.Value = connectionInfo.Address;
            connectionElement.Attributes.Append(addressAttr);

            XmlAttribute portAttr = document.CreateAttribute("port");
            portAttr.Value = connectionInfo.Port;
            connectionElement.Attributes.Append(portAttr);
        }

        private void SensorsToXml(XmlDocument document, XmlElement machineElement, List<SensorMonitoringInfo> machineSensors)
        {
            if (machineSensors?.Any() != true)
                return;

            XmlElement sensorsElement = document.CreateElement("sensors");
            machineElement.AppendChild(sensorsElement);
            foreach (var sensorInfo in machineSensors)
            {
                XmlElement sensorElement = document.CreateElement("sensor");
                sensorsElement.AppendChild(sensorElement);


                XmlAttribute nameAttr = document.CreateAttribute("name");
                nameAttr.Value = sensorInfo.Name;
                sensorElement.Attributes.Append(nameAttr);

                XmlAttribute warningAttr = document.CreateAttribute("warning");
                warningAttr.Value = sensorInfo.ErrorPeriod.Ticks.ToString();
                sensorElement.Attributes.Append(warningAttr);

                XmlAttribute errorAttr = document.CreateAttribute("error");
                errorAttr.Value = sensorInfo.ErrorPeriod.Ticks.ToString();
                sensorElement.Attributes.Append(errorAttr);

                XmlAttribute updateAttr = document.CreateAttribute("update");
                updateAttr.Value = sensorInfo.UpdatePeriod.Ticks.ToString();
                sensorElement.Attributes.Append(updateAttr);
            }
        }

        public string GetSensorAddress(string machine, string sensor, int n)
        {
            return $"{ConnectionInfo.Address}:{ConnectionInfo.Port}/api/sensors/{machine}/{sensor}/{n}";
        }
    }
}
