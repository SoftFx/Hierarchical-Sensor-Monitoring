using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMServer.Core.Converters;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.DataLayer
{
    public class DatabaseAdapter : IDatabaseCore
    {
        #region Size

        //private readonly DatabaseCore _database;

        //public long GetDatabaseSize() => _database.GetDatabaseSize();

        //public long GetMonitoringDataSize() => _database.GetMonitoringDataSize();

        //public long GetEnvironmentDatabaseSize() =>
        //    _database.GetEnvironmentDatabaseSize();

        //#endregion

        //#region Product

        //public void RemoveProduct(string productName) =>
        //    _database.RemoveProduct(productName);

        //public void AddProduct(Product product)
        //{
        //    var entity = product.ConvertToEntity();
        //    _database.AddProduct(entity);
        //}

        //public void UpdateProduct(Product product) => AddProduct(product);

        //public Product GetProduct(string productName)
        //{
        //    var entity = _database.GetProduct(productName);
        //    return entity != null ? new Product(entity) : null;
        //}

        //public List<Product> GetProducts() =>
        //    _database.GetAllProducts()?.Select(e => new Product(e))?.ToList() ?? new List<Product>();

        //#endregion

        //#region Sensors

        //public void RemoveSensor(string productName, string path) =>
        //    _database.RemoveSensor(productName, path);

        //public void AddSensor(SensorInfo info)
        //{
        //    SensorEntity entity = info.ConvertToEntity();
        //    _database.AddSensor(entity);
        //}

        //public void UpdateSensor(SensorInfo info)
        //{
        //    SensorEntity entity = info.ConvertToEntity();
        //    _database.AddSensor(entity);
        //}

        //public void PutSensorData(SensorDataEntity data, string productName) =>
        //    _database.AddSensorValue(data, productName);

        //public SensorDataEntity GetLastSensorValue(string productName, string path) =>
        //    _database.GetLatestSensorValue(productName, path);

        //public SensorInfo GetSensorInfo(string productName, string path)
        //{
        //    var sensorEntity = _database.GetSensorInfo(productName, path);
        //    return sensorEntity != null ? new SensorInfo(sensorEntity) : null;
        //}

        //public List<SensorHistoryData> GetAllSensorHistory(string productName, string path) =>
        //    GetSensorHistoryDatas(_database.GetAllSensorData(productName, path));

        //public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from) =>
        //    GetSensorHistoryDatas(_database.GetSensorData(productName, path, from));

        //public List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to) =>
        //    GetSensorHistoryDatas(_database.GetSensorData(productName, path, from, to));

        //public List<SensorHistoryData> GetSensorHistory(string productName, string path, int n) =>
        //    GetSensorHistoryDatas(_database.GetSensorData(productName, path, n));

        //public SensorHistoryData GetOneValueSensorValue(string productName, string path)
        //{
        //    SensorDataEntity entity = _database.GetLatestSensorValue(productName, path);
        //    return entity != null ? entity.ConvertToHistoryData() : null;
        //}

        //public List<SensorInfo> GetProductSensors(Product product) =>
        //    _database.GetProductSensors(product.Name)?.Select(e => new SensorInfo(e))?.ToList() ?? new List<SensorInfo>();

        //private List<SensorHistoryData> GetSensorHistoryDatas(List<SensorDataEntity> history)
        //{
        //    var historyCount = history?.Count ?? 0;

        //    List<SensorHistoryData> historyDatas = new List<SensorHistoryData>(historyCount);
        //    if (historyCount != 0)
        //        historyDatas.AddRange(history.Select(h => h.ConvertToHistoryData()));

        //    return historyDatas;
        //}

        //#endregion

        //#region User

        //public void AddUser(User user)
        //{
        //    UserEntity entity = user.ConvertToEntity();
        //    _database.AddUser(entity);
        //}

        //public void UpdateUser(User user)
        //{
        //    UserEntity entity = user.ConvertToEntity();
        //    _database.AddUser(entity);
        //}

        //public void RemoveUser(User user)
        //{
        //    UserEntity entity = user.ConvertToEntity();
        //    _database.RemoveUser(entity);
        //}

        //public List<User> GetUsers() => GetUsers(_database.ReadUsers());

        //public List<User> GetUsersPage(int page, int pageSize) =>
        //    GetUsers(_database.ReadUsersPage(page, pageSize));

        //private static List<User> GetUsers(List<UserEntity> userEntities)
        //{
        //    var userEntitiesCount = userEntities?.Count ?? 0;
        //    var users = new List<User>(userEntitiesCount);

        //    if (userEntitiesCount != 0)
        //        users.AddRange(userEntities.Select(e => new User(e)));

        //    return users;
        //}

        //#endregion

        //#region Configuration object

        //public ConfigurationObject GetConfigurationObject(string name)
        //{
        //    var entity = _database.ReadConfigurationObject(name);
        //    return entity != null ? new ConfigurationObject(entity) : null;
        //}

        //public void WriteConfigurationObject(ConfigurationObject obj)
        //{
        //    var entity = obj.ConvertToEntity();
        //    _database.WriteConfigurationObject(entity);
        //}

        //public void RemoveConfigurationObject(string name) => _database.RemoveConfigurationObject(name);

        //#endregion

        //#region Registration ticket

        //public RegistrationTicket ReadRegistrationTicket(Guid id)
        //{
        //    var entity = _database.ReadRegistrationTicket(id);
        //    return entity != null ? new RegistrationTicket(entity) : null;
        //}

        //public void RemoveRegistrationTicket(Guid id) => _database.RemoveRegistrationTicket(id);

        //public void WriteRegistrationTicket(RegistrationTicket ticket)
        //{
        //    var entity = ticket.ConvertToEntity();
        //    _database.WriteRegistrationTicket(entity);
        //}

        #endregion

    }
}
