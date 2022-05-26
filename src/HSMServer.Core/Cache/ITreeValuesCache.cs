using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.SensorsDataValidation;
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
        event Action<ProductModel, TransactionType> ChangeProductEvent;
        event Action<SensorModel, TransactionType> ChangeSensorEvent;


        List<ProductModel> GetTree();
        List<SensorModel> GetSensors();

        ProductModel AddProduct(string productName);
        void RemoveProduct(string id);
        ProductModel GetProduct(string id);
        string GetProductNameById(string id);
        List<ProductModel> GetProductsWithoutParent(User user);

        void UpdateSensor(SensorUpdate updatedSensor);
        void RemoveSensor(Guid sensorId);
        void RemoveSensorsData(string product);
        void RemoveSensorData(Guid sensorId);
        void AddNewSensorValue(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult);
    }
}
