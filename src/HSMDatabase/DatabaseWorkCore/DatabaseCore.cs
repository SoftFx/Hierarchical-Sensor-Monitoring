using System;
using System.Collections.Generic;
using HSMDatabase.DatabaseInterface;
using HSMDatabase.Entity;
using HSMDatabase.EnvironmentDatabase;
using HSMDatabase.SensorsDatabase;

namespace HSMDatabase.DatabaseWorkCore
{
    internal class DatabaseCore : IDatabaseCore
    {
        private readonly IEnvironmentDatabase _environmentDatabase;
        private readonly ITimeDatabaseDictionary _sensorsDatabases;
        private const string DatabaseFolderName = "MonitoringData";
        public DatabaseCore()
        {
            _environmentDatabase = new EnvironmentDatabaseWorker("");
            _sensorsDatabases = new TimeDatabaseDictionary();
        }

        #region Sensors methods

        public List<SensorDataEntity> GetAllSensorData(string productName, string path)
        {
            throw new NotImplementedException();
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime @from)
        {
            throw new NotImplementedException();
        }

        public List<SensorDataEntity> GetSensorData(string productName, string path, DateTime @from, DateTime to)
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

        #endregion

        #region Environment database methods

        #region Products

        public void RemoveProduct(string productName)
        {
            throw new NotImplementedException();
        }

        public void UpdateProduct(ProductEntity productEntity)
        {
            throw new NotImplementedException();
        }

        public void AddProduct(ProductEntity productEntity)
        {
            throw new NotImplementedException();
        }

        public ProductEntity GetProduct(string productName)
        {
            throw new NotImplementedException();
        }

        public List<ProductEntity> GetAllProducts()
        {
            throw new NotImplementedException();
        }

        #endregion


        #endregion

        #region Private methods

        private string CreateSensorsDatabaseName(DateTime from, DateTime to)
        {
            return $"{DatabaseFolderName}_{from.Ticks}_{to.Ticks}";
        }

        #endregion
    }
}
