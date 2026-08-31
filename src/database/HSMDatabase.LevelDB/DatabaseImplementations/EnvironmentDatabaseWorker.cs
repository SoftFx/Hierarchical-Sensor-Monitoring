using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    public sealed class EnvironmentDatabaseWorker : IEnvironmentDatabase
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            IgnoreReadOnlyProperties = true,
        };

        private readonly byte[] _productListKey = "ProductsNames"u8.ToArray();
        private readonly byte[] _accessKeyListKey = "AccessKeys"u8.ToArray();
        private readonly byte[] _sensorIdsKey = "SensorIds"u8.ToArray();
        private readonly byte[] _policyIdsKey = "NewPolicyIds"u8.ToArray();
        private readonly byte[] _folderIdsKey = "FolderIds"u8.ToArray();
        private readonly byte[] _telegramChatIdsKey = "TelegramChats"u8.ToArray();
        private readonly byte[] _slackDestinationIdsKey = "SlackDestinations"u8.ToArray();
        private readonly byte[] _chatIdsKey = "Chats"u8.ToArray();
        private readonly byte[] _alertTemplatesIdsKey = "AlertTemplates"u8.ToArray();
        private readonly byte[] _alertScheduleIdsKey = "AlertSchedule"u8.ToArray();

        private readonly LevelDBDatabaseAdapter _database;
        private readonly Logger _logger;


        public EnvironmentDatabaseWorker(string name)
        {
            _database = new LevelDBDatabaseAdapter(name);
            _logger = LogManager.GetCurrentClassLogger();
        }

        // Used by the restore flow to wrap a pre-built adapter (typically read-only, opened
        // against an unpacked backup) instead of constructing one from a relative-path name.
        public EnvironmentDatabaseWorker(LevelDBDatabaseAdapter adapter)
        {
            _database = adapter;
            _logger = LogManager.GetCurrentClassLogger();
        }


        public TaskResult<string> Backup(string backupPath) => _database.Backup(backupPath);


        #region Folders

        public void PutFolder(FolderEntity entity)
        {
            try
            {
                _database.Put(Encoding.UTF8.GetBytes(entity.Id), JsonSerializer.SerializeToUtf8Bytes(entity));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put folder info for {entity.Id}");
            }
        }

        public void RemoveFolder(string id)
        {
            try
            {
                _database.Delete(Encoding.UTF8.GetBytes(id));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove info for folder {id}");
            }
        }

        public void AddFolderToList(string id)
        {
            try
            {
                var currentList = GetFoldersList();

                if (!currentList.Contains(id))
                    currentList.Add(id);

                _database.Put(_folderIdsKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add folder {id} to list");
            }
        }

        public void RemoveFolderFromList(string id)
        {
            try
            {
                var currentList = GetFoldersList();

                currentList.Remove(id);

                _database.Put(_folderIdsKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove folder {id} from list");
            }
        }

        public FolderEntity GetFolder(string id)
        {
            try
            {
                return _database.TryRead(Encoding.UTF8.GetBytes(id), out byte[] value)
                    ? JsonSerializer.Deserialize<FolderEntity>(value)
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for folder {id}");
            }

            return null;
        }

        public List<string> GetFoldersList()
        {
            try
            {
                return _database.TryRead(_folderIdsKey, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(value)
                    : new();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get folders ids list");
            }

            return new();
        }

        #endregion

        #region Products

        public void AddProductToList(string productId)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out var value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                if (!currentList.Contains(productId))
                    currentList.Add(productId);

                _database.Put(_productListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add product to list");
            }
        }

        public List<string> GetProductsList()
        {
            var result = new List<string>();
            try
            {
                var products = _database.TryRead(_productListKey, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                result.AddRange(products);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get products list");
            }

            return result;
        }

        public ProductEntity GetProduct(string id)
        {
            var bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<ProductEntity>(Encoding.UTF8.GetString(value)) : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for product {id}");
            }

            return null;
        }

        public void PutProduct(ProductEntity product)
        {
            var bytesKey = Encoding.UTF8.GetBytes(product.Id);

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(product));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put product info for {product.Id}");
            }
        }

        public void RemoveProduct(string id)
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove info for product {id}");
            }
        }

        public void RemoveProductFromList(string productId)
        {
            try
            {
                var currentList = _database.TryRead(_productListKey, out byte[] value)
                    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                currentList.Remove(productId);

                _database.Put(_productListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove product {productId} from list");
            }
        }

        #endregion

        #region AccessKey

        public void AddAccessKeyToList(string id)
        {
            try
            {
                var currentList = GetAccessKeyList();
                if (!currentList.Contains(id))
                    currentList.Add(id);

                _database.Put(_accessKeyListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add AccessKey {id} to list");
            }
        }

        public List<string> GetAccessKeyList()
        {
            var result = new List<string>();
            try
            {
                var keys = _database.TryRead(_accessKeyListKey, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new List<string>();

                result.AddRange(keys);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get AccessKeys list");
            }

            return result;
        }

        public void RemoveAccessKeyFromList(string id)
        {
            try
            {
                var currentList = GetAccessKeyList();
                currentList.Remove(id);

                _database.Put(_accessKeyListKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove AccessKey {id} from list");
            }
        }

        public void AddAccessKey(AccessKeyEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);

            try
            {
                _database.Put(bytesKey, JsonSerializer.SerializeToUtf8Bytes(entity));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to put AccessKey for {entity.Id}");
            }
        }

        public void RemoveAccessKey(string id)
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove AccessKey by {id}");
            }
        }

        public AccessKeyEntity GetAccessKey(string id)
        {
            var bytesKey = Encoding.UTF8.GetBytes(id);
            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<AccessKeyEntity>(Encoding.UTF8.GetString(value)) : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read AccessKey by {id}");
            }

            return null;
        }

        #endregion

        #region Sensors

        public void AddSensorIdToList(string sensorId)
        {
            void AddSensorIdToListIfNotExist(List<string> sensorIds)
            {
                if (!sensorIds.Contains(sensorId))
                    sensorIds.Add(sensorId);
            }

            UpdateSensorIdsList(AddSensorIdToListIfNotExist, $"Failed to add sensor id {sensorId} to list");
        }

        public void AddSensor(SensorEntity entity)
        {
            var bytesKey = Encoding.UTF8.GetBytes(entity.Id);
            var bytesValue = JsonSerializer.SerializeToUtf8Bytes(entity);

            try
            {
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add sensor info for {entity.Id}");
            }
        }

        public void RemoveSensorIdFromList(string sensorId) =>
            UpdateSensorIdsList(sensorIdsList => sensorIdsList.Remove(sensorId),
                                $"Failed to remove sensor id {sensorId} from list");

        public void RemoveSensor(string sensorId)
        {
            byte[] bytesKey = Encoding.UTF8.GetBytes(sensorId);

            try
            {
                _database.Delete(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove sensor info for {sensorId}");
            }
        }

        public SensorEntity GetSensorEntity(string sensorId)
        {
            var bytesKey = Encoding.UTF8.GetBytes(sensorId);

            try
            {
                return _database.TryRead(bytesKey, out byte[] value)
                    ? JsonSerializer.Deserialize<SensorEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for sensor {sensorId}");
            }

            return null;
        }

        public List<string> GetAllSensorsIds() =>
            GetListOfKeys(_sensorIdsKey, "Failed to get sensors ids list");

        private void UpdateSensorIdsList(Action<List<string>> updateListAction, string errorMessage)
        {
            try
            {
                var sensorIds = GetAllSensorsIds();

                updateListAction?.Invoke(sensorIds);

                _database.Put(_sensorIdsKey, JsonSerializer.SerializeToUtf8Bytes(sensorIds));
            }
            catch (Exception e)
            {
                _logger.Error(e, errorMessage);
            }
        }

        #endregion

        #region Policies

        public void AddPolicyIdToList(Guid policyId)
        {
            try
            {
                var policyIds = GetAllPoliciesIds();

                if (!policyIds.Select(g => new Guid(g)).Contains(policyId))
                    policyIds.Add(policyId.ToByteArray());

                _database.Put(_policyIdsKey, JsonSerializer.SerializeToUtf8Bytes(policyIds));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add policy id {policyId} to list");
            }
        }

        public void AddPolicy(PolicyEntity entity)
        {
            var value = JsonSerializer.SerializeToUtf8Bytes(entity, _options);

            try
            {
                _database.Put(entity.Id, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add policy info for {entity.Id}");
            }
        }

        public void RemovePolicy(Guid policyId)
        {
            try
            {
                var policyIds = GetAllPoliciesIds();

                for (int i = 0; i < policyIds.Count; i++)
                    if (new Guid(policyIds[i]) == policyId)
                    {
                        policyIds.RemoveAt(i);
                        break;
                    }

                _database.Put(_policyIdsKey, JsonSerializer.SerializeToUtf8Bytes(policyIds));
                _database.Delete(policyId.ToByteArray());
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove Policy by {policyId}");
            }
        }

        public List<byte[]> GetAllPoliciesIds() => GetListOfBytes(_policyIdsKey, "Failed to get all policy ids");

        public PolicyEntity GetPolicy(byte[] policyId)
        {
            try
            {
                return _database.TryRead(policyId, out byte[] value)
                       ? JsonSerializer.Deserialize<PolicyEntity>(value)
                       : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for policy {policyId}");
            }

            return null;
        }

        #endregion

        #region User

        public void AddUser(UserEntity user)
        {
            var userKey = PrefixConstants.GetUniqueUserKey(user.UserName);
            var keyBytes = Encoding.UTF8.GetBytes(userKey);

            try
            {
                _database.Put(keyBytes, JsonSerializer.SerializeToUtf8Bytes(user));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to save user {user.UserName}");
            }
        }

        public List<UserEntity> ReadUsers()
        {
            var key = PrefixConstants.GetUsersReadKey();
            var keyBytes = Encoding.UTF8.GetBytes(key);
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                List<byte[]> values = _database.GetAllStartingWith(keyBytes);
                foreach (var value in values)
                {
                    try
                    {
                        users.Add(JsonSerializer.Deserialize<UserEntity>(Encoding.UTF8.GetString(value)));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to UserEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read users!");
            }

            return users;
        }

        public void RemoveUser(UserEntity user)
        {
            var userKey = PrefixConstants.GetUniqueUserKey(user.UserName);
            var keyBytes = Encoding.UTF8.GetBytes(userKey);
            try
            {
                _database.Delete(keyBytes);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to delete user '{user.UserName}'");
            }
        }

        public List<UserEntity> ReadUsersPage(int page, int pageSize)
        {
            var key = PrefixConstants.GetUsersReadKey();
            var keyBytes = Encoding.UTF8.GetBytes(key);
            List<UserEntity> users = new List<UserEntity>();
            try
            {
                List<byte[]> values = _database.GetPageStartingWith(keyBytes, page, pageSize);
                foreach (var value in values)
                {
                    try
                    {
                        users.Add(JsonSerializer.Deserialize<UserEntity>(Encoding.UTF8.GetString(value)));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to UserEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read users!");
            }

            return users;
        }

        #endregion

        #region Telegram chats

        public List<byte[]> GetTelegramChatsList() => GetListOfBytes(_telegramChatIdsKey, "Failed to get telegram chats ids list");

        public TelegramChatEntity GetTelegramChat(byte[] chatId)
        {
            try
            {
                return _database.TryRead(chatId, out byte[] value)
                    ? JsonSerializer.Deserialize<TelegramChatEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for telegram chat {new Guid(chatId)}");
            }

            return null;
        }

        #endregion

        #region Slack destinations

        public List<byte[]> GetSlackDestinationsList() => GetListOfBytes(_slackDestinationIdsKey, "Failed to get slack destinations ids list");

        public SlackDestinationEntity GetSlackDestination(byte[] id)
        {
            try
            {
                return _database.TryRead(id, out byte[] value)
                    ? JsonSerializer.Deserialize<SlackDestinationEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for slack destination {new Guid(id)}");
            }

            return null;
        }

        #endregion

        #region Chats

        public List<byte[]> GetChatsList() => GetListOfBytes(_chatIdsKey, "Failed to get chats ids list");

        public ChatEntity GetChat(byte[] chatId)
        {
            try
            {
                return _database.TryRead(chatId, out byte[] value)
                    ? JsonSerializer.Deserialize<ChatEntity>(Encoding.UTF8.GetString(value))
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for chat {new Guid(chatId)}");
            }

            return null;
        }

        public void AddChat(ChatEntity chat)
        {
            try
            {
                _database.Put(chat.Id, JsonSerializer.SerializeToUtf8Bytes(chat));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add chat info for {chat.Id}");
            }
        }

        public void RemoveChat(byte[] chatId)
        {
            try
            {
                _database.Delete(chatId);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove info for chat {new Guid(chatId)}");
            }
        }

        public void AddChatToList(byte[] chatId)
        {
            try
            {
                var currentList = GetChatsList();

                if (!currentList.Any(existing => existing.SequenceEqual(chatId)))
                    currentList.Add(chatId);

                _database.Put(_chatIdsKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add chat id to list");
            }
        }

        public void RemoveChatFromList(byte[] chatId)
        {
            try
            {
                var currentList = GetChatsList();

                currentList.RemoveAll(existing => existing.SequenceEqual(chatId));

                _database.Put(_chatIdsKey, JsonSerializer.SerializeToUtf8Bytes(currentList));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove chat id {chatId} from list");
            }
        }

        #endregion

        #region AlertTemplates

        public void AddAlertTemplateIdToList(byte[] id)
        {
            try
            {
                var ids = GetAllAlertTemplatesIds();

                if (!ids.Any(existingId => existingId.SequenceEqual(id)))
                {
                    ids.Add(id);
                    _database.Put(_alertTemplatesIdsKey, JsonSerializer.SerializeToUtf8Bytes(ids));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add Alert template id {id} to list");
            }
        }

        public void AddAlertTemplate(AlertTemplateEntity entity)
        {
            try
            {
                _database.Put(entity.Id, JsonSerializer.SerializeToUtf8Bytes(entity, _options));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add alert template info for {entity.Id}");
            }
        }

        public void RemoveAlertTemplate(byte[] id)
        {
            try
            {

                var ids = GetAllAlertTemplatesIds();
                ids.RemoveAll(x => new Guid(x) == new Guid(id));
     
                _database.Put(_alertTemplatesIdsKey, JsonSerializer.SerializeToUtf8Bytes(ids));
                _database.Delete(id);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove Alert template by {id}");
            }
        }

        public List<byte[]> GetAllAlertTemplatesIds() => GetListOfBytes(_alertTemplatesIdsKey, "Failed to get all alert template Ids");


        public AlertTemplateEntity GetAlertTemplate(byte[] id)
        {
            try
            {
                return _database.TryRead(id, out byte[] value)
                       ? JsonSerializer.Deserialize<AlertTemplateEntity>(value)
                       : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for alert template {id}");
            }

            return null;
        }

        #endregion


        #region Alert Schedule
        public void AddAlertScheduleIdToList(byte[] id)
        {
            try
            {
                var ids = GetAllAlertScheduleIds();

                if (!ids.Any(existingId => existingId.SequenceEqual(id)))
                {
                    ids.Add(id);
                    _database.Put(_alertScheduleIdsKey, JsonSerializer.SerializeToUtf8Bytes(ids));
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add alert schedule id {id} to list");
            }
        }

        public void AddAlertSchedule(AlertScheduleEntity entity)
        {
            try
            {
                _database.Put(entity.Id, JsonSerializer.SerializeToUtf8Bytes(entity, _options));
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add alert schedule info for {entity.Id}");
            }
        }

        public void RemoveAlertSchedule(byte[] id)
        {
            try
            {

                var ids = GetAllAlertScheduleIds();
                ids.RemoveAll(x => new Guid(x) == new Guid(id));

                _database.Put(_alertScheduleIdsKey, JsonSerializer.SerializeToUtf8Bytes(ids));
                _database.Delete(id);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove alert schedule by {id}");
            }
        }

        public List<byte[]> GetAllAlertScheduleIds() => GetListOfBytes(_alertScheduleIdsKey, "Failed to get all alert schedule Ids");


        public AlertScheduleEntity GetAlertSchedule(byte[] id)
        {
            try
            {
                return _database.TryRead(id, out byte[] value)
                       ? JsonSerializer.Deserialize<AlertScheduleEntity>(value)
                       : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read info for alert schedule {id}");
            }

            return null;
        }

        #endregion

        #region Api tokens

        // Key layout: "ApiToken_<tokenId>" rows hold ApiTokenEntity values, revocation
        // generations live under "ApiTokenGeneration_*". The prefixes differ after the
        // "ApiToken" stem, so the ReadAllApiTokens scan cannot pick generation rows up.
        // Unlike the neighboring regions, the Api token write paths (insert/rotate/put/
        // generation advances) do NOT swallow storage failures: they must be observable by
        // the caller so that a failed write leaves neither durable nor live state.
        // RemoveApiToken reports its outcome as a bool for the same reason — retention
        // must not unpublish a live record whose durable row may still exist. The boot
        // scan (ReadAllApiTokens) propagates scan failures for the fail-closed load.
        //
        // Token rows are serialized with the parameterless JsonSerializer overload ON
        // PURPOSE: the file's shared _options apply DefaultIgnoreCondition =
        // WhenWritingDefault, which would omit schema-shape fields carrying their type's
        // default value. A token row must be the exact, explicit image of its record so
        // the fail-closed load never judges a row on serializer-default artifacts.

        private const string ApiTokenPrefix = "ApiToken_";
        private const string ApiTokenGlobalGenerationKey = "ApiTokenGeneration_Global";
        private const string ApiTokenOwnerGenerationPrefix = "ApiTokenGeneration_Owner_";

        // Serializes the check-then-write token sequences inside the store boundary so that
        // an existence check and a write can never interleave from two threads.
        private readonly object _apiTokenLock = new();


        private static byte[] GetApiTokenKey(string tokenId) => Encoding.UTF8.GetBytes(ApiTokenPrefix + tokenId);


        // Persist-first creation primitive: the TokenId existence check and the row write run
        // under _apiTokenLock, never as an unlocked read plus Put. Returns false when the
        // TokenId already exists — the caller must discard the whole candidate and retry with
        // a completely new id/secret pair. Throws ServerDatabaseException on write failure,
        // leaving no durable state behind.
        public bool TryInsertApiToken(ApiTokenEntity entity)
        {
            // A null TokenId would write the bare "ApiToken_" prefix key, which the
            // ReadAllApiTokens scan then picks up — validate before touching the store.
            if (entity?.TokenId is null)
                throw new ArgumentException("API token insert requires a token id.", nameof(entity));

            lock (_apiTokenLock)
            {
                var key = GetApiTokenKey(entity.TokenId);

                if (_database.TryRead(key, out _))
                    return false;

                _database.Put(key, JsonSerializer.SerializeToUtf8Bytes(entity));

                return true;
            }
        }

        // Atomic rotation: writes the revoked predecessor and its replacement in a single
        // LevelDB batch so no reader can observe the pair half-rotated. Same collision and
        // failure contract as TryInsertApiToken.
        public bool TryRotateApiToken(ApiTokenEntity revokedOld, ApiTokenEntity replacement)
        {
            // Same bare-prefix-key hazard as insert: validate both rows before writing.
            if (revokedOld?.TokenId is null || replacement?.TokenId is null)
                throw new ArgumentException("API token rotation requires token ids on both rows.");

            lock (_apiTokenLock)
            {
                var replacementKey = GetApiTokenKey(replacement.TokenId);

                if (_database.TryRead(replacementKey, out _))
                    return false;

                _database.PutBatch(
                [
                    (GetApiTokenKey(revokedOld.TokenId), JsonSerializer.SerializeToUtf8Bytes(revokedOld)),
                    (replacementKey, JsonSerializer.SerializeToUtf8Bytes(replacement)),
                ]);

                return true;
            }
        }

        // Full-row update for lifecycle transitions (revoke, restrict). Propagates storage
        // failures: a silently dropped revocation would resurface after restart.
        public void PutApiToken(ApiTokenEntity entity)
        {
            // Same bare-prefix-key hazard as insert and removal.
            if (entity?.TokenId is null)
                throw new ArgumentException("API token update requires a token id.", nameof(entity));

            lock (_apiTokenLock)
            {
                _database.Put(GetApiTokenKey(entity.TokenId), JsonSerializer.SerializeToUtf8Bytes(entity));
            }
        }

        // Single-record read for the authentication path. A read or deserialize failure logs
        // and returns null, which the caller must treat as a failed (closed) authentication.
        public ApiTokenEntity GetApiToken(string tokenId)
        {
            try
            {
                // Span overload: no per-row string allocation, and a non-UTF8 row reaches
                // the JSON parser as bytes instead of silently becoming replacement chars.
                return _database.TryRead(GetApiTokenKey(tokenId), out byte[] value)
                    ? JsonSerializer.Deserialize<ApiTokenEntity>(value)
                    : null;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to read API token by token id");
            }

            return null;
        }

        // Durable removal for retention. True means the row is gone from the store —
        // deleted, or already absent (LevelDB deletes are idempotent). False means the
        // removal failed and the row may still exist: the caller must NOT unpublish the
        // live record in that case, or the row would rejoin the authentication index
        // after the next restart.
        public bool RemoveApiToken(string tokenId)
        {
            // A null TokenId would target the bare "ApiToken_" prefix key.
            if (tokenId is null)
                throw new ArgumentException("API token removal requires a token id.", nameof(tokenId));

            try
            {
                lock (_apiTokenLock)
                {
                    _database.Delete(GetApiTokenKey(tokenId));
                }

                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to remove API token by token id; the durable row may still exist");

                return false;
            }
        }

        // Full scan used to rebuild the in-memory authentication index at startup. The
        // scan itself propagates storage failures: an unreadable token region must fail
        // the whole index closed (an empty result is a fresh install, not a silent
        // outage). A corrupt individual record is skipped and logged; a skipped record
        // simply never authenticates.
        public List<ApiTokenEntity> ReadAllApiTokens()
        {
            var tokens = new List<ApiTokenEntity>();

            var values = _database.GetAllStartingWith(Encoding.UTF8.GetBytes(ApiTokenPrefix));

            foreach (var value in values)
            {
                try
                {
                    // Span overload: the boot scan must not allocate a string per row.
                    tokens.Add(JsonSerializer.Deserialize<ApiTokenEntity>(value));
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to deserialize an ApiTokenEntity, record skipped");
                }
            }

            return tokens;
        }

        // Durable revocation generations. Missing state reads as 0 (fresh installation
        // baseline); corrupt state — unparsable or negative, the counter is monotonic
        // from 0 — throws so the caller can fail authentication closed. Advance persists
        // the new generation before it is published to authentication.

        public long GetGlobalRevocationGeneration() => ReadRevocationGeneration(ApiTokenGlobalGenerationKey);

        public long AdvanceGlobalRevocationGeneration()
        {
            lock (_apiTokenLock)
                return WriteRevocationGeneration(ApiTokenGlobalGenerationKey, ReadRevocationGeneration(ApiTokenGlobalGenerationKey) + 1);
        }

        public long GetOwnerRevocationGeneration(Guid ownerUserId) =>
            ReadRevocationGeneration(ApiTokenOwnerGenerationPrefix + ownerUserId);

        public long AdvanceOwnerRevocationGeneration(Guid ownerUserId)
        {
            var key = ApiTokenOwnerGenerationPrefix + ownerUserId;

            lock (_apiTokenLock)
                return WriteRevocationGeneration(key, ReadRevocationGeneration(key) + 1);
        }

        private long ReadRevocationGeneration(string key)
        {
            if (!_database.TryRead(Encoding.UTF8.GetBytes(key), out byte[] value))
                return 0;

            if (!long.TryParse(Encoding.UTF8.GetString(value), out var generation) || generation < 0)
                throw new ServerDatabaseException($"Corrupt revocation generation state under key {key}");

            return generation;
        }

        private long WriteRevocationGeneration(string key, long generation)
        {
            _database.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(generation.ToString()));

            return generation;
        }

        #endregion

        public void Compact()
        {
            _database.Compact();
        }

        public void Dispose() => _database.Dispose();

        private List<string> GetListOfKeys(byte[] key, string error)
        {
            try
            {
                return _database.TryRead(key, out byte[] value) ?
                    JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(value))
                    : new();
            }
            catch (Exception e)
            {
                _logger.Error(e, error);
            }

            return new();
        }

        private List<byte[]> GetListOfBytes(byte[] key, string error)
        {
            try
            {
                return _database.TryRead(key, out byte[] value) ?
                    JsonSerializer.Deserialize<List<byte[]>>(Encoding.UTF8.GetString(value))
                    : new();
            }
            catch (Exception e)
            {
                _logger.Error(e, error);
            }

            return new();
        }
    }

}
