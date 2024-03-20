
using System;
using HSMServer.Core.Model;
using HSMServer.Middleware;

namespace HSMServer.Services
{
    public interface IPermissionService
    {
        bool CheckPermission(FilterRequestData data, KeyPermissions permissions, out string message);
        bool TryGetKey(Guid id, out AccessKeyModel key, out string message);
        bool TryGetProduct(Guid id, out ProductModel product, out string message);
    }
}
