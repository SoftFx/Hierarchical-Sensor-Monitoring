using HSMSensorDataObjects;
using HSMServer.Core.Model;
using HSMServer.Middleware;
using System.Collections.Generic;

namespace HSMServer.Services
{
    public interface IPermissionService
    {
        bool CheckPermission(PublicApiRequestInfo data, SensorData sensorData, KeyPermissions permissions, out string message);

        IEnumerable<T> GetPendingChecked<T>(PublicApiRequestInfo requestData, KeyPermissions permissions) where T : BaseRequest;
    }
}
