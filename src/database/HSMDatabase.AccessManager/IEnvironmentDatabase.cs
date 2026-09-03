using System;
using System.Collections.Generic;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;


namespace HSMDatabase.AccessManager
{
    public interface IEnvironmentDatabase : IDisposable
    {
        TaskResult<string> Backup(string backupPath);


        #region Folders

        void PutFolder(FolderEntity entity);
        void RemoveFolder(string id);
        void AddFolderToList(string id);
        void RemoveFolderFromList(string id);
        FolderEntity GetFolder(string id);
        List<string> GetFoldersList();

        #endregion

        #region Products

        void AddProductToList(string productName);
        List<string> GetProductsList();
        ProductEntity GetProduct(string id);
        void PutProduct(ProductEntity product);
        void RemoveProduct(string id);
        void RemoveProductFromList(string productName);

        #endregion

        #region AccessKey

        void AddAccessKeyToList(string id);
        List<string> GetAccessKeyList();
        void RemoveAccessKeyFromList(string id);
        void AddAccessKey(AccessKeyEntity entity);
        void RemoveAccessKey(string id);
        AccessKeyEntity GetAccessKey(string id);

        #endregion

        #region Api tokens

        // Persist-first token creation: false on TokenId collision (retry with a fresh
        // id/secret pair), throws on write failure leaving no durable state.
        bool TryInsertApiToken(ApiTokenEntity entity);

        // Atomic old-revoke + replacement-insert in one write batch; same collision/failure
        // contract as TryInsertApiToken.
        bool TryRotateApiToken(ApiTokenEntity revokedOld, ApiTokenEntity replacement);

        // Full-row lifecycle update (revoke, restrict); propagates storage failures.
        void PutApiToken(ApiTokenEntity entity);

        // Single-record read for authentication; null means missing or unreadable (fail closed).
        ApiTokenEntity GetApiToken(string tokenId);

        // Retention removal. True = the durable row is gone (deleted or already absent);
        // false = the removal failed and the row may still exist — the caller must NOT
        // unpublish the live record in that case, or the row rejoins the index after restart.
        bool RemoveApiToken(string tokenId);

        // Full scan to rebuild the authentication index at startup. Corrupt records are
        // skipped; a scan failure throws so the caller can fail the index closed. Each
        // record comes with the token id of the key it was stored under, so the loader
        // can reject a row whose key disagrees with its payload TokenId.
        List<(string KeyTokenId, ApiTokenEntity Entity)> ReadAllApiTokens();

        long GetGlobalRevocationGeneration();
        long AdvanceGlobalRevocationGeneration();
        long GetOwnerRevocationGeneration(Guid ownerUserId);
        long AdvanceOwnerRevocationGeneration(Guid ownerUserId);

        // Append-only per-request security events (authentication success/failure,
        // authorization denial). Key embeds the timestamp so the scan is chronological;
        // the random EventId suffix makes the key collision-free.
        void PutApiTokenSecurityEvent(ApiTokenSecurityEventEntity entity);

        // Chronological scan of all security events (retention/query surface).
        List<ApiTokenSecurityEventEntity> ReadApiTokenSecurityEvents();

        // Bounded retention removal: up to `limit` events strictly older than the cutoff
        // ticks (chronological key order), returns the number removed.
        int RemoveApiTokenSecurityEventsBefore(long ticksCutoffUtc, int limit);

        #endregion

        #region Sensors

        void AddSensorIdToList(string sensorId);
        void AddSensor(SensorEntity info);
        void RemoveSensorIdFromList(string sensorId);
        void RemoveSensor(string sensorId);
        SensorEntity GetSensorEntity(string sensorId);
        List<string> GetAllSensorsIds();

        #endregion

        #region Policy

        List<byte[]> GetAllPoliciesIds();
        PolicyEntity GetPolicy(byte[] policyId);
        void AddPolicyIdToList(Guid policyId);
        void AddPolicy(PolicyEntity policy);
        void RemovePolicy(Guid policyId);
        #endregion

        #region Users

        void AddUser(UserEntity user);
        List<UserEntity> ReadUsers();
        void RemoveUser(UserEntity user);
        List<UserEntity> ReadUsersPage(int page, int pageSize);

        #endregion

        #region Telegram chats

        List<byte[]> GetTelegramChatsList();
        TelegramChatEntity GetTelegramChat(byte[] chatId);

        #endregion

        #region Slack destinations

        List<byte[]> GetSlackDestinationsList();
        SlackDestinationEntity GetSlackDestination(byte[] id);

        #endregion

        #region Chats

        List<byte[]> GetChatsList();
        ChatEntity GetChat(byte[] chatId);
        void AddChat(ChatEntity chat);
        void RemoveChat(byte[] chatId);
        void AddChatToList(byte[] chatId);
        void RemoveChatFromList(byte[] chatId);

        #endregion

        #region Alert templates

        List<byte[]> GetAllAlertTemplatesIds();
        AlertTemplateEntity GetAlertTemplate(byte[] id);
        void AddAlertTemplateIdToList(byte[] id);
        void AddAlertTemplate(AlertTemplateEntity alertTemplate);
        void RemoveAlertTemplate(byte[] id);
        #endregion

        #region Alert Schedules
        List<byte[]> GetAllAlertScheduleIds();
        AlertScheduleEntity GetAlertSchedule(byte[] id);
        void AddAlertScheduleIdToList(byte[] id);
        void AddAlertSchedule(AlertScheduleEntity alertScheduleEntity);
        void RemoveAlertSchedule(byte[] id);
        #endregion Alert Schedules

        void Compact();
    }
}
