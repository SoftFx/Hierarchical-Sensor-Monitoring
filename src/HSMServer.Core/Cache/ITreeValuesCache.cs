using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache.Entities;
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
        event Action<SensorModel> UploadSensorDataEvent;


        List<ProductModel> GetTree();

        void AddNewSensorValue(SensorValueBase sensorValue, DateTime timeCollected, ValidationResult validationResult);
    }
}
