using HSMServer.BackgroundServices;
using HSMServer.Core.Model;
using HSMServer.Services;

namespace HSMServer.Middleware
{
    public sealed class ReadPermissionFilter(IPermissionService service, DataCollectorWrapper collector) : PermissionFilter(service, collector)
    {
        protected override KeyPermissions Permissions => KeyPermissions.CanReadSensorData;
    }
}
