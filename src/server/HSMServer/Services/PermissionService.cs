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
            message = string.Empty;
            
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
            var pathParts = PermissionFilter.GetPathParts(sensorData.Path);
            
            for (int i = 0; i < pathParts.Length; i++)
            {
                var expectedName = pathParts[i];

                if (i != pathParts.Length - 1)
                {
                    product = product?.SubProducts.FirstOrDefault(sp => sp.Value.DisplayName == expectedName).Value;

                    if (product == null &&
                        !TreeValuesCache.TryCheckAccessKeyPermissions(data.Key, KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors, out message))
                        return false;
                }
                else
                {
                    sensor = product?.Sensors.FirstOrDefault(s => s.Value.DisplayName == expectedName).Value;

                    if (sensor == null &&
                        !TreeValuesCache.TryCheckAccessKeyPermissions(data.Key, KeyPermissions.CanAddSensors, out message))
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
    }
}
