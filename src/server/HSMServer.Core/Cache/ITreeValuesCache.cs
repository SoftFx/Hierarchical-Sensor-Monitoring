using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Cache
{
    public enum ActionType
    {
        Add,
        Update,
        Delete,
        Init,
        Clear
    }


    public interface ITreeValuesCache
    {
        event Action<ProductModel, ActionType> ChangeProductEvent;
        event Action<BaseSensorModel, ActionType> ChangeSensorEvent;
        event Action<AccessKeyModel, ActionType> ChangeAccessKeyEvent;

        event Action<PolicyResult> ChangePolicyResultEvent;

        List<BaseSensorModel> GetSensors();
        List<AccessKeyModel> GetAccessKeys();

        ProductModel AddProduct(string productName, Guid authorId);
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
        List<AccessKeyModel> GetMasterKeys();

        void UpdateSensor(SensorUpdate updatedSensor);
        void RemoveSensor(Guid sensorId);
        void UpdateMutedSensorState(Guid sensorId, DateTime? endOfMuting = null);
        void ClearSensorHistory(Guid sensorId, DateTime to);
        void CheckSensorHistory(Guid sensorId);
        void ClearNodeHistory(Guid productId);
        BaseSensorModel GetSensor(Guid sensorId);

        IAsyncEnumerable<List<BaseValue>> GetSensorValues(HistoryRequestModel request);
        IAsyncEnumerable<List<BaseValue>> GetSensorValuesPage(Guid sensorId, DateTime from, DateTime to, int count);

        IAsyncEnumerable<List<JournalRecordModel>> GetJournalValuesPage(Guid id, DateTime from, DateTime to, JournalType journalType, int count);
        void AddJournal(JournalRecordModel journalRecordModel);

        void UpdateCacheState();

        void SaveLastStateToDb();
    }
}
