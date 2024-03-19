using HSMServer.Core.Cache;
using HSMServer.Core.Interfaces.Services;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Middleware;
using System;

namespace HSMServer.Services
{
    public class PermissionService
    {
        private readonly ITreeValuesCache _cache;
        
        
        public PermissionService(ITreeValuesCache cache)
        {
            _cache = cache;
        }

        public bool CheckPermission(FilterRequestData data, KeyPermissions permissions, out string message)
        {
            message = string.Empty;
            //TODO: move method from HSMServer.Core
            return true;
        }
    }
}
