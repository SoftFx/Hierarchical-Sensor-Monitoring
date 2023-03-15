using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Cache
{
    public enum ActionType
    {
        Add,
        Update,
        Delete,
    }


    public interface ITreeValuesCache
    {
        bool IsInitialized { get; }

        event Action<ProductModel, ActionType> ChangeProductEvent;
        event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;

        event Action<BaseSensorModel, PolicyResult> NotifyAboutChangesEvent;


        List<ProductModel> GetTree();
        List<BaseSensorModel> GetSensors();
        List<AccessKeyModel> GetAccessKeys();

        ProductModel AddProduct(string productName);
        void UpdateProduct(ProductModel product);
        void UpdateProduct(ProductUpdate product);
        void RemoveProduct(Guid id);
        ProductModel GetProduct(Guid id);
        ProductModel GetProductByName(string name);
        string GetProductNameById(Guid id);
        List<ProductModel> GetProducts();

        bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message);
        bool TryCheckKeyReadPermissions(BaseRequestModel request, out string message);

        AccessKeyModel AddAccessKey(AccessKeyModel key);
        AccessKeyModel RemoveAccessKey(Guid id);
        AccessKeyModel UpdateAccessKey(AccessKeyUpdate key);
        AccessKeyModel UpdateAccessKeyState(Guid id, KeyState state);
        AccessKeyModel GetAccessKey(Guid id);

        void UpdateSensor(SensorUpdate updatedSensor);
        void RemoveSensor(Guid sensorId);
        void UpdateMutedSensorState(Guid sensorId, DateTime? endOfMuting = null);
        void RemoveNode(Guid product);
        void ClearSensorHistory(Guid sensorId);
        void ClearNodeHistory(Guid productId);
        BaseSensorModel GetSensor(Guid sensorId);
        void NotifyAboutChanges(BaseSensorModel model, PolicyResult oldStatus);

        IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request);
        IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count);

        void UpdateCacheState();
    }
}
