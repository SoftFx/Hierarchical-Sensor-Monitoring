using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.LevelDB;

namespace HSMDatabase.DatabaseWorkCore
{
    public sealed class DatabaseCore
    {
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly ITimeDatabaseDictionary _sensorsDatabases;
        private readonly IDatabaseSettings _databaseSettings;


        public DatabaseCore(IDatabaseSettings dbSettings)
        {
            _databaseSettings = dbSettings;
            _environmentDatabase = LevelDBManager.GetEnvitonmentDatabaseInstance(_databaseSettings.GetPathToEnvironmentDatabase());
            _sensorsDatabases = new TimeDatabaseDictionary(_environmentDatabase, dbSettings);

            OpenAllExistingSensorDatabases();
        }


        private void OpenAllExistingSensorDatabases()
        {
            List<string> databasesList = _environmentDatabase.GetMonitoringDatabases();
            foreach (var databaseName in databasesList)
            {
                GetDatesFromFolderName(databaseName, out DateTime from, out DateTime to);
                if (from != DateTime.MinValue && to != DateTime.MinValue)
                {
                    ISensorsDatabase database = LevelDBManager.GetSensorDatabaseInstance(
                        _databaseSettings.GetPathToMonitoringDatabase(databaseName), from, to);
                    _sensorsDatabases.AddDatabase(database);
                }
            }
        }

        #region Database size

        public long GetDatabaseSize()
        {
            DirectoryInfo databasesDir = new DirectoryInfo(_databaseSettings.DatabaseFolder);
            return GetDirectorySize(databasesDir);
        }

        public long GetMonitoringDataSize()
        {
            long size = 0;
            var databasesList = _environmentDatabase.GetMonitoringDatabases();
            foreach (var monitoringDB in databasesList)
            {
                DirectoryInfo info = new DirectoryInfo(_databaseSettings.GetPathToMonitoringDatabase(monitoringDB));
                size += GetDirectorySize(info);
            }

            return size;
        }

        public long GetEnvironmentDatabaseSize()
        {
            DirectoryInfo environmentDatabaseDir =
                new DirectoryInfo(_databaseSettings.GetPathToEnvironmentDatabase());
            return GetDirectorySize(environmentDatabaseDir);
        }

        private long GetDirectorySize(DirectoryInfo directory)
        {
            long size = 0;
            FileInfo[] files = directory.GetFiles();
            foreach (var file in files)
            {
                try
                {
                    size += file.Length;
                }
                catch { }
            }

            DirectoryInfo[] directories = directory.GetDirectories();
            foreach (var dir in directories)
            {
                size += GetDirectorySize(dir);
            }

            return size;
        }

        #endregion

        #region Sensors methods

        public List<SensorDataEntity> GetAllSensorData(string productName, string path)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                result.AddRange(database.GetAllSensorValues(productName, path));
            }

            return result;
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, int n)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            databases.Reverse();
            foreach (var database in databases)
            {
                result.AddRange(database.GetAllSensorValues(productName, path));
                if (result.Count >= n)
                    break;
            }

            result.Sort((d1, d2) =>
                d1.TimeCollected.CompareTo(d2.TimeCollected));
            return result.TakeLast(n).ToList();
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                //Skip too old data
                if (database.DatabaseMaxTicks < from.Ticks)
                    continue;

                result.AddRange(database.GetSensorValuesFrom(productName, path, from));
            }

            result.RemoveAll(r => r.TimeCollected < from);
            return result;
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from, DateTime to)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                //Skip too old data
                if (database.DatabaseMaxTicks < from.Ticks)
                    continue;

                //Skip too new data
                if (database.DatabaseMinTicks > to.Ticks)
                    continue;

                result.AddRange(database.GetSensorValuesBetween(productName, path, from, to));
            }

            return result;
        }

        public long GetSensorSize(string productName, string path)
        {
            long size = 0L;
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                size += database.GetSensorSize(productName, path);
            }

            return size;
        }

        public void AddSensorValue(SensorDataEntity entity, string productName)
        {
            var database = _sensorsDatabases.GetDatabase(entity.TimeCollected);
            database.PutSensorData(entity, productName);
        }

        public SensorDataEntity GetLatestSensorValue(string productName, string path)
        {
            List<ISensorsDatabase> databases = _sensorsDatabases.GetAllDatabases();
            databases.Reverse();
            foreach (var database in databases)
            {
                var currentLatestValue = database.GetLatestSensorValue(productName, path);
                if (currentLatestValue != null)
                {
                    return currentLatestValue;
                }
            }

            return null;
        }

        #endregion

        #region Environment database : Sensor

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            return _environmentDatabase.GetSensorInfo(productName, path);
        }

        public List<SensorEntity> GetProductSensors(string productName)
        {
            List<SensorEntity> sensors = new List<SensorEntity>();
            List<string> sensorPaths = _environmentDatabase.GetSensorsList(productName);
            foreach (var path in sensorPaths)
            {
                SensorEntity sensorEntity = _environmentDatabase.GetSensorInfo(productName, path);
                if (sensorEntity != null)
                    sensors.Add(sensorEntity);
            }

            return sensors;
        }

        public void RemoveSensor(string productName, string path)
        {
            //TAM-90: Do not delete metadata when delete sensors
            //_environmentDatabase.RemoveSensor(productName, path);
            //_environmentDatabase.RemoveSensorFromList(productName, path);
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                database.DeleteAllSensorValues(productName, path);
            }
        }

        public void AddSensor(SensorEntity entity)
        {
            _environmentDatabase.AddNewSensorToList(entity.ProductName, entity.Path);
            _environmentDatabase.AddSensor(entity);
        }

        public List<SensorEntity> GetOldAllSensors() => _environmentDatabase.GetOldSensorsInfo();

        #endregion

        #region Environment database : Product

        public void RemoveProduct(string productName)
        {
            _environmentDatabase.RemoveProductInfo(productName);
            _environmentDatabase.RemoveProductFromList(productName);
            var sensorsList = _environmentDatabase.GetSensorsList(productName);
            _environmentDatabase.RemoveSensorsList(productName);
            foreach (var sensor in sensorsList)
            {
                RemoveSensor(productName, sensor);
            }
        }

        public void UpdateProduct(ProductEntity productEntity)
        {
            AddProduct(productEntity);
        }

        public void AddProduct(ProductEntity productEntity)
        {
            _environmentDatabase.AddProductToList(productEntity.DisplayName);
            _environmentDatabase.PutProductInfo(productEntity);
        }

        public ProductEntity GetProduct(string productName)
        {
            return _environmentDatabase.GetProductInfo(productName);
        }

        public List<ProductEntity> GetAllProducts()
        {
            //move OldProducts to this
            List<ProductEntity> products = new List<ProductEntity>();
            var productNames = _environmentDatabase.GetProductsList();
            foreach (var productName in productNames)
            {
                var product = _environmentDatabase.GetProductInfo(productName);
                if (product != null)
                    products.Add(product);
            }

            return products;
        }

        public List<string> GetOldAllProducts()
        {
            var products = new List<string>();
            var productNames = _environmentDatabase.GetProductsList();

            if (productNames == null || productNames.Count == 0) return null;

            foreach (var productName in productNames)
            {
                var product = _environmentDatabase.GetOldProductInfo(productName);
                if (product != null)
                    products.Add(product);
            }

            return products;
        }

        #endregion

        #region Environment database : User

        public void AddUser(UserEntity user)
        {
            _environmentDatabase.AddUser(user);
        }

        public List<UserEntity> ReadUsers()
        {
            return _environmentDatabase.ReadUsers().ToList();
        }

        public void RemoveUser(UserEntity user)
        {
            _environmentDatabase.RemoveUser(user);
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            return _environmentDatabase.ReadUsersPage(page, pageSize).ToList();
        }

        #endregion

        #region Environment database : Configuration

        public ConfigurationEntity ReadConfigurationObject(string name)
        {
            return _environmentDatabase.ReadConfigurationObject(name);
        }

        public void WriteConfigurationObject(ConfigurationEntity obj)
        {
            _environmentDatabase.WriteConfigurationObject(obj);
        }

        public void RemoveConfigurationObject(string name)
        {
            _environmentDatabase.RemoveConfigurationObject(name);
        }

        #endregion

        #region Environment database : Ticket

        public RegisterTicketEntity ReadRegistrationTicket(Guid id)
        {
            return _environmentDatabase.ReadRegistrationTicket(id);
        }

        public void RemoveRegistrationTicket(Guid id)
        {
            _environmentDatabase.RemoveRegistrationTicket(id);
        }

        public void WriteRegistrationTicket(RegisterTicketEntity ticket)
        {
            _environmentDatabase.WriteRegistrationTicket(ticket);
        }

        #endregion

        #region Private methods

        private void GetDatesFromFolderName(string folder, out DateTime from, out DateTime to)
        {
            var splitResults = folder.Split('_');
            bool success1 = long.TryParse(splitResults[1], out long fromTicks);
            from = success1 ? new DateTime(fromTicks) : DateTime.MinValue;
            bool success2 = long.TryParse(splitResults[2], out long toTicks);
            to = success2 ? new DateTime(toTicks) : DateTime.MinValue;
        }

        #endregion

        public void Dispose()
        {
            _environmentDatabase.Dispose();
            _sensorsDatabases.GetAllDatabases().ForEach(d => d.Dispose());
        }
    }
}