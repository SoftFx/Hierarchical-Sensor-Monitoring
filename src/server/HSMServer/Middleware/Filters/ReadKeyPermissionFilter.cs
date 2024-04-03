using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Services;

namespace HSMServer.Middleware
{
    public sealed class ReadKeyPermissionFilter(IPermissionService service, ITreeValuesCache cache, DataCollectorWrapper collector) : KeyPermissionFilterBase(service, cache, collector)
    {
        protected override KeyPermissions Permissions => KeyPermissions.CanReadSensorData;
    }
}
