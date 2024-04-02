using HSMSensorDataObjects;
using HSMServer.Core.Model;
using HSMServer.Middleware;
using System.Collections.Generic;

namespace HSMServer.Services
{
    public interface IPermissionService
    {
        bool CheckPermission(RequestData data, SensorData sensorData, KeyPermissions permissions, out string message);

        IEnumerable<T> GetPendingChecked<T>(RequestData requestData, KeyPermissions permissions) where T : BaseRequest;
    }
}
