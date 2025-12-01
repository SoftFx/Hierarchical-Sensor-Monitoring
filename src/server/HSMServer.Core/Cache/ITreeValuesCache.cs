using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.StatisticInfo;
using HSMServer.Core.TableOfChanges;


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
        event Action<string, int, int> RequestProcessed;

        event Action<AlertMessage> NewAlertMessageEvent;
        event Action<FolderEventArgs> FillFolderChats;

        int SensorsCount { get; }


        List<BaseSensorModel> GetSensors();
        List<BaseSensorModel> GetSensors(string wildcard, SensorType? type, Guid? folderId);
        List<AccessKeyModel> GetAccessKeys();

        Task<ProductModel> AddProductAsync(string productName, Guid authorId, CancellationToken token = default);
        Task UpdateProductAsync(ProductUpdate product, CancellationToken token = default);
        Task RemoveProductAsync(Guid id, InitiatorInfo initiator = null, CancellationToken token = default);
        ProductModel GetProduct(Guid id);
        ProductModel GetProductByName(string name);
        bool TryGetProductByName(string name, out ProductModel product);
        bool TryGetProductNameById(Guid id, out string name);
        List<ProductModel> GetProducts();
        List<ProductModel> GetAllNodes();

        //bool TryCheckKeyWritePermissions(BaseUpdateRequest request, out string message);
        //bool TryCheckKeyReadPermissions(BaseUpdateRequest request, out string message);
        //bool TryCheckSensorUpdateKeyPermission(BaseUpdateRequest request, out Guid sensorId, out string message);

        AccessKeyModel AddAccessKey(AccessKeyModel key);
        AccessKeyModel RemoveAccessKey(Guid id);
        
        AccessKeyModel UpdateAccessKey(AccessKeyUpdate key);
        AccessKeyModel UpdateAccessKeyState(Guid id, KeyState state);
        AccessKeyModel GetAccessKey(Guid id);
        List<AccessKeyModel> GetMasterKeys();
        
        bool TryGetKey(Guid id, out AccessKeyModel key, out string message);
        bool TryGetRootProduct(Guid id, out ProductModel product, out string message);
        bool TryGetProduct(Guid productId, out ProductModel product);
        bool TryGetProduct(string productId, out ProductModel product);

        void SetLastKeyUsage(Guid key, string ip);


        Task<TaskResult> AddSensorValueAsync(Guid accessKey, Guid productId, SensorValueBase value, CancellationToken token = default);
        Task<Dictionary<string, string>> AddSensorValuesAsync(Guid key, Guid productId, IEnumerable<SensorValueBase> values, CancellationToken token = default);

        Task<TaskResult> AddOrUpdateSensorAsync(SensorAddOrUpdateRequest request, CancellationToken token = default);
        //Task<TaskResult> UpdateSensor(SensorUpdate updatedSensor, out string error);

        Task<TaskResult> UpdateSensorAsync(SensorUpdate updatedSensor);

        bool TryGetSensorByPath(Guid productId, string path, out BaseSensorModel sensor);
        Task<TaskResult> UpdateSensorValueAsync(UpdateSensorValueRequestModel request, CancellationToken token = default);
        Task RemoveSensorAsync(Guid sensorId, InitiatorInfo initiator = null, Guid? parentId = null, CancellationToken token = default);
        Task UpdateMutedSensorStateAsync(Guid sensorId, InitiatorInfo initiator, DateTime? endOfMuting = null);
        Task ClearSensorHistoryAsync(ClearHistoryRequest request, CancellationToken token = default);
        Task CheckSensorsHistoryAsync(CancellationToken token = default);
        Task ClearNodeHistoryAsync(ClearHistoryRequest request, CancellationToken token = default);

        BaseSensorModel GetSensor(Guid sensorId);
        IEnumerable<BaseSensorModel> GetSensorsByFolder(HashSet<Guid> folderIds = null);


        IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request);
        IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count, RequestOptions requestOptions = default);

        SensorHistoryInfo GetSensorHistoryInfo(Guid sensorId);
        NodeHistoryInfo GetNodeHistoryInfo(Guid nodeId);

        //void UpdateCacheState();

        Task ClearEmptyNodesAsync(ProductModel product, CancellationToken token = default);

        Task SaveLastStateToDbAsync(CancellationToken token = default);

        void RemoveChatsFromPolicies(Guid folderId, List<Guid> chats, InitiatorInfo initiator);

        List<AlertTemplateModel> GetAlertTemplateModels();

        Task AddAlertTemplateAsync(AlertTemplateModel model, CancellationToken tocken = default);

        AlertTemplateModel GetAlertTemplate(Guid id);

        Task RemoveAlertTemplateAsync(Guid id, CancellationToken token = default);

        Task RunSensorsSelfDestroyAsync(CancellationToken token = default);

        Task RunProductsSelfDestroyAsync(CancellationToken token = default);
    }
}