using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Services;

namespace HSMServer.Middleware
{
    public sealed class SendPermissionFilter(IPermissionService service,ITreeValuesCache cache, DataCollectorWrapper collector) : PermissionFilter(service, cache, collector)
    {
        protected override KeyPermissions Permissions => KeyPermissions.CanSendSensorData;
    }
}
