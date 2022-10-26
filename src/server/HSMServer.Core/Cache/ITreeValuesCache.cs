using HSMServer.Core.Cache.UpdateEntitites;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsUpdatesQueue;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Cache
{
    public enum TransactionType
    {
        Add,
        Update,
        Delete,
    }


    public interface ITreeValuesCache
    {
        bool IsInitialized { get; }

        event Action<ProductModel, TransactionType> ChangeProductEvent;
        event Action<BaseSensorModel, TransactionType> ChangeSensorEvent;
        event Action<AccessKeyModel, TransactionType> ChangeAccessKeyEvent;

        event Action<BaseSensorModel, ValidationResult> NotifyAboutChangesEvent;


        List<ProductModel> GetTree();
        List<BaseSensorModel> GetSensors();
        List<AccessKeyModel> GetAccessKeys();

        ProductModel AddProduct(string productName);
        void UpdateProduct(ProductModel product);
        void RemoveProduct(string id);
        ProductModel GetProduct(string id);
        string GetProductNameById(string id);
        List<ProductModel> GetProducts(User user, bool isAllProducts = false);

        bool TryCheckKeyWritePermissions(StoreInfo storeInfo, out string message);
        bool TryCheckKeyReadPermissions(StoreInfo storeInfo, out string message);

        AccessKeyModel AddAccessKey(AccessKeyModel key);
        void RemoveAccessKey(Guid id);
        AccessKeyModel UpdateAccessKey(AccessKeyUpdate key);
        AccessKeyModel GetAccessKey(Guid id);

        void UpdateSensor(SensorUpdate updatedSensor);
        void RemoveSensor(Guid sensorId);
        void RemoveSensorsData(string product);
        void RemoveSensorData(Guid sensorId);
        BaseSensorModel GetSensor(Guid sensorId);
        BaseSensorModel GetSensor(StoreInfo info);
        void NotifyAboutChanges(BaseSensorModel model, ValidationResult oldStatus);

        List<BaseValue> GetSensorValues(Guid sensorId, int count);
        List<BaseValue> GetSensorValues(Guid sensorId, DateTime from, DateTime to);

        void UpdatePolicy(TransactionType type, Policy policy);
    }
}
