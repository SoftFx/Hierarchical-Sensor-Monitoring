using HSMServer.BackgroundServices;
using HSMServer.Core.Interfaces.Services;
using HSMServer.Core.Model;

namespace HSMServer.Middleware
{
    public sealed class ReadPermissionFilter(IPermissionService cache, DataCollectorWrapper collector) : PermissionFilter(cache, collector)
    {
        protected override KeyPermissions Permissions => KeyPermissions.CanReadSensorData;
    }
}
