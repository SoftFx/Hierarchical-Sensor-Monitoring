using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Middleware;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Services
{
    public class PermissionService(ITreeValuesCache cache) : IPermissionService
    {
        private readonly List<SensorData> _pendingCheck = new(1 << 2);


        public bool CheckPermission(RequestData data, SensorData sensorData, KeyPermissions permissions, out string message)
        {
            if (data.Key is not null)
                return CheckKeyPermission(data, sensorData, permissions, out message);
            
            if (TryGetKey(sensorData.KeyId, out var key, out message) && cache.TryGetProduct(key.ProductId, out var product, out message))
            {
                data.Key = key;
                data.Product = product;
                data.BuildTelemetryPath();
            }
            else
            {
                _pendingCheck.Add(sensorData);
                return false;
            }

            return CheckKeyPermission(data, sensorData, permissions, out message);
        }

        private bool CheckKeyPermission(RequestData data, SensorData sensorData, KeyPermissions permissions, out string message)
        {
            if (CheckPermissions(data, sensorData, permissions, out message))
            {
                data.Data.Add(sensorData);
                return true;
            }

            return false;
        }

        public IEnumerable<T> GetPendingChecked<T>(RequestData requestData, KeyPermissions permissions)
        {
            if (requestData.Key is null)
                return [];

            return _pendingCheck.Where(x => CheckKeyPermission(requestData, x, permissions, out _)).Select(x => x.Request) as IEnumerable<T>;
        }

        private bool CheckPermissions(RequestData data, SensorData sensorData, KeyPermissions permissions, out string message)
        {
            message = string.Empty;

            if (!data.Key.IsValid(permissions, out message))
                return false;
            
            BaseSensorModel sensor = null;

            var product = data.Product;
            var key = data.Key;

            var pathParts = PermissionFilter.GetPathParts(sensorData.Path);

            for (int i = 0; i < pathParts.Length; i++)
            {
                var expectedName = pathParts[i];

                if (i != pathParts.Length - 1)
                {
                    product = product?.SubProducts.FirstOrDefault(sp => sp.Value.DisplayName == expectedName).Value;

                    if (product == null &&
                        !TreeValuesCache.TryCheckAccessKeyPermissions(key, KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors, out message))
                        return false;
                }
                else
                {
                    sensor = product?.Sensors.FirstOrDefault(s => s.Value.DisplayName == expectedName).Value;

                    if (sensor == null &&
                        !TreeValuesCache.TryCheckAccessKeyPermissions(key, KeyPermissions.CanAddSensors, out message))
                        return false;
                }
            }

            sensorData.Id = sensor?.Id ?? Guid.Empty;

            return true;
        }

        private bool TryGetKey(string id, out AccessKeyModel key, out string message)
        {
            key = null;

            if (!Guid.TryParse(id, out var keyId))
            {
                message = "Invalid key";
                return false;
            }

            return cache.TryGetKey(keyId, out key, out message);
        }
    }
}
