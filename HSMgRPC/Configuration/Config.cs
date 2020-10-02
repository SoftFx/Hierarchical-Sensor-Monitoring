using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Grpc.Core;
using HSMCommon;
//using HSMServer.Authentication;
using NLog;

namespace HSMServer.Configuration
{
    public static class Config
    {
        #region Sync objects

        private static object _sensorsDictionarySync = new object();
        private static object _usersSync = new object();
        private static object _certificateSync = new object();

        #endregion

        #region Private fields

        private static Dictionary<string, ValueTuple<string, string>> _sensorsDictionary;
        //private static List<User> _users;
        public const string ConfigFolderName = "Config";
        public const string CertificatesFolderName = "Certificates";
        private const string _monitoringConfigFileName = "monitoringConfig.xml";
        private const string _usersFileName = "users.xml";
        private const string _serverCertName = "hsm.server.pfx";
        private static Logger _logger;
        private static int _usersCount = 10;
        private static string _configFilePath;
        private static string _usersFilePath;
        public static string CertificatesFolderPath;

        private static X509Certificate2 _serverCertificate;
        //private static Encryptor _encryptor;

        #endregion



        static Config()
        {
            _logger = LogManager.GetCurrentClassLogger();
            //_encryptor = new Encryptor(Environment.MachineName);

            string configFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName);
            CertificatesFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName,
                CertificatesFolderName);
            if (!Directory.Exists(configFolderPath))
            {
                FileManager.SafeCreateDirectory(configFolderPath);
            }

            _configFilePath = Path.Combine(configFolderPath, _monitoringConfigFileName);
            if (!File.Exists(_configFilePath))
            {
                FileManager.SafeCreateFile(_configFilePath);
            }

            _usersFilePath = Path.Combine(configFolderPath, _usersFileName);
            if (!File.Exists(_usersFilePath))
            {
                FileManager.SafeCreateFile(_usersFilePath);
            }
        }

        public const string JOB_SENSOR_PREFIX = "JobSensorValue";
        private static string ServerCertificatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            ConfigFolderName,  CertificatesFolderName, _serverCertName);
        //public static List<User> Users
        //{
        //    get
        //    {
        //        lock (_usersSync)
        //        {
        //            if (_users == null)
        //            {
        //                InitializeUsers();
        //            }

        //            return _users;
        //        }
        //    }
        //}

        public static Dictionary<string, (string, string)> SensorsDictionary
        {
            get
            {
                lock (_sensorsDictionarySync)
                {
                    if (_sensorsDictionary == null)
                    {
                        InitializeSensorsDictionary();
                    }

                    return _sensorsDictionary;
                }
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

        private static X509Certificate2 ReadServerCertificate()
        {
            //return CertificateReader.ReadCertificateFromPEMCertAndKey(ServerCertPath, ServerKeyPath);
            X509Certificate2 certificate =  new X509Certificate2(ServerCertificatePath);

            

            return certificate;
        }

        //private static void InitializeUsers()
        //{
        //    try
        //    {
        //        lock (_usersSync)
        //        {
        //            _users = new List<User>();
        //        }
        //        LoadUsers(_usersFilePath);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.Error("Failed to load users!");
        //    }
        //}

        private static void LoadUsers(string usersFilePath)
        {
            XmlDocument document = new XmlDocument();
            document.Load(usersFilePath);


        }

        private static void ParseXmlUsers(XmlDocument document)
        {
            XmlNodeList nodes = document.SelectNodes("//users/user");

            if (nodes == null)
                return;

            foreach (XmlNode node in nodes)
            {
                string login = string.Empty;
                string password = string.Empty;

                XmlAttribute loginAttr = node.Attributes?["login"];
                if (loginAttr != null)
                    login = loginAttr.Value;
                    //login = _encryptor.Decrypt(loginAttr.Value);

                XmlAttribute passwordAttr = node.Attributes?["password"];
                if (passwordAttr != null)
                    password = passwordAttr.Value;
                    //password = _encryptor.Decrypt(passwordAttr.Value);

            }
        }

        private static void InitializeSensorsDictionary()
        {
            lock (_sensorsDictionarySync)
            {
                _sensorsDictionary = new Dictionary<string, (string, string)>();
            }
            ReadConfig();
        }

        public static void InitializeConfig()
        {
            ReadConfig();
        }

        private static void ReadConfig()
        {
            try
            {
                LoadConfig(_configFilePath);
                _usersCount = 10;
                lock (_sensorsDictionarySync)
                {
                    _logger.Info($"{_sensorsDictionary?.Count} sensors read from config");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load config");
            }
        }
        public static int UsersCount
        {
            get { return _usersCount; }
        }

        private static void LoadConfig(string configFilePath)
        {
            XmlDocument document = new XmlDocument();
            document.Load(configFilePath);

            ParseXmlConfig(document);
        }

        private static void ParseXmlConfig(XmlDocument document)
        {
            XmlNodeList nodes = document.SelectNodes("//machines/machine");

            if (nodes == null)
                return;

            foreach (XmlNode node in nodes)
            {
                string machineName = string.Empty;

                XmlAttribute machineNameAttr = node.Attributes["name"];
                if (machineNameAttr != null)
                    machineName = machineNameAttr.Value;

                if(string.IsNullOrEmpty(machineName))
                    continue;

                foreach (XmlNode childNode in node.ChildNodes)
                {
                    if (childNode.Name == "sensors")
                    {
                        ParseSensorsNode(childNode, machineName);
                    }
                }
            }
        }

        private static void ParseSensorsNode(XmlNode sensorsNode, string machineName)
        {
            foreach (XmlNode childNode in sensorsNode.ChildNodes)
            {
                string name = string.Empty;
                string key = string.Empty;

                if (childNode.Attributes == null)
                    continue;

                XmlAttribute nameAttr = childNode.Attributes["name"];
                if (nameAttr != null)
                    name = nameAttr.Value;

                XmlAttribute keyAttr = childNode.Attributes["key"];
                if (keyAttr != null)
                    //key = _encryptor.Decrypt(keyAttr.Value);
                    key = keyAttr.Value;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(key))
                {
                    lock (_sensorsDictionarySync)
                    {
                        _sensorsDictionary[key] = (machineName, name);
                    }
                }
            }
        }
        private static void SaveConfigFile()
        {
            string content = ConfigToXml();

            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName, _monitoringConfigFileName);

            try
            {
                FileManager.SafeDelete(file);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting current connections file");
                throw;
            }

            try
            {
                FileManager.SafeWriteText(file, content);
                _logger.Info("Config file saved successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save config file!");
                throw;
            }
            
        }

        private static string ConfigToXml()
        {
            XmlDocument document = new XmlDocument();
            XmlElement rootElement = document.CreateElement("machines");
            document.AppendChild(rootElement);

            List<string> machines = new List<string>();

            lock (_sensorsDictionarySync)
            {
                machines.AddRange(_sensorsDictionary.Select(s => s.Value.Item1).Distinct()); 
            }

            foreach (var machine in machines)
            {
                XmlElement machineElement = document.CreateElement("machine");
                rootElement.AppendChild(machineElement);

                XmlAttribute machineNameAttribute = document.CreateAttribute("name");
                machineNameAttribute.Value = machine;
                machineElement.Attributes.Append(machineNameAttribute);

                List<(string, string)> keySensor = new List<(string, string)>();
                lock (_sensorsDictionarySync)
                {
                    keySensor.AddRange(_sensorsDictionary.Where(s => s.Value.Item1 == machine).Select(s => (s.Value.Item2, s.Key)));
                }

                if (keySensor.Count < 1)
                    continue;

                XmlElement sensorsElement = document.CreateElement("sensors");
                machineElement.AppendChild(sensorsElement);

                foreach (var sensor in keySensor)
                {
                    XmlElement sensorElement = document.CreateElement("sensor");
                    sensorsElement.AppendChild(sensorElement);

                    XmlAttribute sensorNameAttribute = document.CreateAttribute("name");
                    sensorNameAttribute.Value = sensor.Item1;
                    sensorElement.Attributes.Append(sensorNameAttribute);

                    XmlAttribute sensorKeyAttribute = document.CreateAttribute("key");
                    //sensorKeyAttribute.Value = _encryptor.Encrypt(sensor.Item2);
                    sensorKeyAttribute.Value = sensor.Item2;
                    sensorElement.Attributes.Append(sensorKeyAttribute);
                }
            }

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
        }

        public static bool IsKeyRegistered(string key)
        {
            bool result;
            lock (_sensorsDictionarySync)
            {
                result = SensorsDictionary.ContainsKey(key);
            }

            return result;
        }

        public static string GenerateDataStorageKey(string key)
        {
            (string, string) machineSensor = (string.Empty, string.Empty);
            lock (_sensorsDictionarySync)
            {
                if(_sensorsDictionary.ContainsKey(key))
                    machineSensor = _sensorsDictionary[key];
            }

            if (string.IsNullOrEmpty(machineSensor.Item1) || string.IsNullOrEmpty(machineSensor.Item2))
                return string.Empty;

            return $"{JOB_SENSOR_PREFIX}_{machineSensor.Item1}_{machineSensor.Item2}_{DateTime.Now.Ticks.ToString()}";
        }

        public static string GenerateSearchKey(string machineName)
        {
            return machineName;
        }

        public static string GenerateSearchKey(string machineName, string sensorName)
        {
            return $"{machineName}_{sensorName}";
        }

        public static void Dispose()
        {
            //SaveConfigFile();
            _logger = null;
        }
    }
}
