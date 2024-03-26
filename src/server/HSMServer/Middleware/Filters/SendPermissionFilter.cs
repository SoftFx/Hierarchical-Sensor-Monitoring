using HSMServer.BackgroundServices;
using HSMServer.Core.Model;
using HSMServer.Services;

namespace HSMServer.Middleware
{
    public sealed class SendPermissionFilter(IPermissionService service, DataCollectorWrapper collector) : PermissionFilter(service, collector)
    {
        protected override KeyPermissions Permissions => KeyPermissions.CanSendSensorData;
    }
}
