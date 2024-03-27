using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Middleware;
using System;
using System.Linq;

namespace HSMServer.Services
{
    public class PermissionService(ITreeValuesCache cache) : IPermissionService
    {
        public bool CheckPermission(RequestData data, SensorData sensorData, KeyPermissions permissions, out string message)
        {
            if (CheckInitPermissions(data, sensorData, out message, out var sensor))
            {
                sensorData.Id = sensor?.Id ?? Guid.Empty;
                if (data.Key.IsValid(permissions, out message))
                {
                    data.Data.Add(sensorData);
                    return true;
                }

                return false;
            };

            return false;
        }

        private bool CheckInitPermissions(RequestData data, SensorData sensorData, out string message, out BaseSensorModel sensor)
        {
            message = string.Empty;
            sensor = null;

            var product = data.Product;
            var key = data.Key;
            
            if (key is null && !TryGetKey(sensorData.KeyId, out key, out message))
                return false;
            
            if (product is null && !TryGetProduct(key.ProductId, out product, out message))
                return false;

            sensorData.Key = key;
            
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

            return true;
        }

        public bool TryGetKey(Guid id, out AccessKeyModel key, out string message)
        {
            return cache.TryGetKey(id, out key, out message);
        }

        public bool TryGetProduct(Guid id, out ProductModel product, out string message)
        {
            return cache.TryGetProduct(id, out product, out message);
        }
        
        private bool TryGetKey(string id, out AccessKeyModel key, out string message)
        {
            key = null;

            if (!Guid.TryParse(id, out var keyId))
            {
                message = "Invalid key";
                return false;
            }

            return TryGetKey(keyId, out key, out message);
        }
    }
}
