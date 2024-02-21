using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;
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
        event Action<ProductModel, ActionType> ChangeProductEvent;
        event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;

        event Action<AlertMessage> NewAlertMessageEvent;

        List<BaseSensorModel> GetSensors();
        List<AccessKeyModel> GetAccessKeys();

        ProductModel AddProduct(string productName, Guid authorId);
        void UpdateProduct(ProductUpdate product);
        void RemoveProduct(Guid id, InitiatorInfo initiator = null);
        ProductModel GetProduct(Guid id);
        ProductModel GetProductByName(string name);
        bool TryGetProductByName(string name, out ProductModel product);
        string GetProductNameById(Guid id);
        List<ProductModel> GetProducts();
        List<ProductModel> GetAllNodes();

        bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message);
        bool TryCheckKeyReadPermissions(BaseRequestModel request, out string message);
        bool TryCheckSensorUpdateKeyPermission(BaseRequestModel request, out Guid sensorId, out string message);

        AccessKeyModel AddAccessKey(AccessKeyModel key);
        AccessKeyModel RemoveAccessKey(Guid id);
        AccessKeyModel UpdateAccessKey(AccessKeyUpdate key);
        AccessKeyModel UpdateAccessKeyState(Guid id, KeyState state);
        AccessKeyModel GetAccessKey(Guid id);
        List<AccessKeyModel> GetMasterKeys();

        bool TryAddOrUpdateSensor(SensorAddOrUpdateRequestModel update, out string error);
        bool TryUpdateSensor(SensorUpdate updatedSensor, out string error);
        bool TryGetSensorByPath(string product, string path, out BaseSensorModel sensor);
        void UpdateSensorValue(UpdateSensorValueRequestModel request);
        void RemoveSensor(Guid sensorId, InitiatorInfo initiator = null, Guid? parentId = null);
        void UpdateMutedSensorState(Guid sensorId, InitiatorInfo initiator, DateTime? endOfMuting = null);
        void ClearSensorHistory(ClearHistoryRequest request);
        void CheckSensorHistory(Guid sensorId);
        void ClearNodeHistory(ClearHistoryRequest request);
        BaseSensorModel GetSensor(Guid sensorId);
        IEnumerable<BaseSensorModel> GetSensorsByFolder(HashSet<Guid> folderIds = null);


        IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request);
        IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count, RequestOptions requestOptions = default);

        SensorHistoryInfo GetSensorHistoryInfo(Guid sensorId);
        NodeHistoryInfo GetNodeHistoryInfo(Guid nodeId);

        void UpdateCacheState();

        void SaveLastStateToDb();

        void RemoveChatsFromPolicies(Guid folderId, List<Guid> chats, InitiatorInfo initiator);
    }
}