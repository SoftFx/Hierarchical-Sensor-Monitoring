using HSMDatabase.DatabaseInterface;
using HSMDatabase.Entity;
using HSMDatabase.EnvironmentDatabase;
using HSMDatabase.SensorsDatabase;
using System;
using System.Collections.Generic;

namespace HSMDatabase.DatabaseWorkCore
{
    public class DatabaseCore : IDatabaseCore
    {
        #region Singleton

        private static volatile DatabaseCore _instance;
        private static readonly object _singletonLockObj = new object();
        public static IDatabaseCore GetInstance()
        {
            return Instance;
        }

        private static DatabaseCore Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLockObj)
                    {
                        if (_instance == null)
                        {
                            _instance = new DatabaseCore();
                        }
                    }
                }

                return _instance;
            }
        }
        #endregion

        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly ITimeDatabaseDictionary _sensorsDatabases;
        private const string DatabaseFolderName = "MonitoringData";
        private const string EnvironmentDatabaseName = "EnvironmentData";
        private DatabaseCore()
        {
            _environmentDatabase = new EnvironmentDatabaseWorker(EnvironmentDatabaseName);
            _sensorsDatabases = new TimeDatabaseDictionary();

        }

        private void OpenAllExistingSensorDatabases()
        {

        }

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

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                //Skip too old data
                if (database.DatabaseMaxTicks < from.Ticks)
                    continue;

                //If from is in the middle of database, add just values starting from 'from' DateTime
                if (database.DatabaseMinTicks < from.Ticks)
                    result.AddRange(database.GetSensorValuesFrom(productName, path, from));

                //Add all values from databases that begin after the 'from' time
                result.AddRange(database.GetAllSensorValues(productName, path));
            }

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

                //Read data if all the period is from one database
                if (database.DatabaseMinTicks < from.Ticks && database.DatabaseMaxTicks > to.Ticks)
                {
                    result.AddRange(database.GetSensorValuesBetween(productName, path, from, to));
                    break;
                }

                //Period starts inside the database
                if (database.DatabaseMinTicks < from.Ticks)
                {
                    result.AddRange(database.GetSensorValuesFrom(productName, path, from));
                    continue;
                }

                //Period ends inside the database
                if (database.DatabaseMaxTicks > to.Ticks)
                {
                    result.AddRange(database.GetSensorValuesBetween(productName, path, database.DatabaseMinDateTime, to));
                    break;
                }

                //Database period is fully inside the 'from'-'to' period
                result.AddRange(database.GetAllSensorValues(productName, path));
            }

            return result;
        }

        public SensorEntity GetSensorInfo(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorEntity> GetProductSensors(string productName)
        {
            throw new NotImplementedException();
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
            bool isExists = _sensorsDatabases.TryGetDatabase(entity.TimeCollected, out var database);
            if (isExists)
            {
                database.PutSensorData(entity, productName);
                return;
            }

            DateTime minDateTime = DateTimeMethods.GetMinDateTime(entity.TimeCollected);
            DateTime maxDateTime = DateTimeMethods.GetMaxDateTime(entity.TimeCollected);
            string newDatabaseName = CreateSensorsDatabaseName(minDateTime, maxDateTime);
            ISensorsDatabase newDatabase = new SensorsDatabaseWorker(newDatabaseName, minDateTime, maxDateTime);
            _sensorsDatabases.AddDatabase(newDatabase);
            newDatabase.PutSensorData(entity, productName);
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

        public void RemoveSensor(string productName, string path)
        {
            //TODO: write this method
            _environmentDatabase.RemoveSensor(productName, path);
            var databases = _sensorsDatabases.GetAllDatabases();
            foreach (var database in databases)
            {
                database.DeleteAllSensorValues(productName, path);  
            }
        }

        public void AddSensor(SensorEntity entity)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Environment database : Product

        public void RemoveProduct(string productName)
        {
            _environmentDatabase.RemoveProductInfo(productName);
            _environmentDatabase.RemoveProductFromList(productName);
        }

        public void UpdateProduct(ProductEntity productEntity)
        {
            AddProduct(productEntity);
        }

        public void AddProduct(ProductEntity productEntity)
        {
            _environmentDatabase.AddProductToList(productEntity.Name);
            _environmentDatabase.PutProductInfo(productEntity);
        }

        public ProductEntity GetProduct(string productName)
        {
            return _environmentDatabase.GetProductInfo(productName);
        }

        public List<ProductEntity> GetAllProducts()
        {
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
        
        #endregion

        #region Environment database : User

        public void AddUser(UserEntity user)
        {
            _environmentDatabase.AddUser(user);
        }

        public List<UserEntity> ReadUsers()
        {
            return _environmentDatabase.ReadUsers();
        }

        public void RemoveUser(UserEntity user)
        {
            _environmentDatabase.RemoveUser(user);
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            return _environmentDatabase.ReadUsersPage(page, pageSize);
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

        private string CreateSensorsDatabaseName(DateTime from, DateTime to)
        {
            return $"{DatabaseFolderName}_{from.Ticks}_{to.Ticks}";
        }

        #endregion
    }
}
