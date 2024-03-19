using System;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;

namespace HSMServer.Core.Interfaces.Services;

public interface IPermissionService
{
    bool CheckAddPermissions(ProductModel product, AccessKeyModel accessKey, ReadOnlySpan<string> pathParts, out string message);

    bool CheckPermission(ProductModel product, AccessKeyModel accessKey, ReadOnlySpan<string> pathParts, KeyPermissions permissions, out string message);
    
    bool TryCheckKeyWritePermissions(BaseRequestModel request, out string message);
    bool TryCheckKeyReadPermissions(BaseRequestModel request, out string message);
    bool TryCheckSensorUpdateKeyPermission(BaseRequestModel request, out Guid sensorId, out string message);
}