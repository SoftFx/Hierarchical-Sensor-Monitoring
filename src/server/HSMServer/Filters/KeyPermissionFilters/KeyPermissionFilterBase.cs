using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Threading.Tasks;

namespace HSMServer.Middleware;


public sealed class ReadDataKeyPermissionFilter : KeyPermissionFilterBase
{
    public ReadDataKeyPermissionFilter() : base(KeyPermissions.CanReadSensorData) { }
}


public sealed class SendDataKeyPermissionFilter : KeyPermissionFilterBase
{
    public SendDataKeyPermissionFilter() : base(KeyPermissions.CanSendSensorData) { }
}


public abstract class KeyPermissionFilterBase(KeyPermissions _permissions) : ActionFilterAttribute
{
    public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.TryGetPublicApiInfo(out var info) && !info.Key.IsValid(_permissions, out var error))
        {
            await context.HttpContext.SetAccessError(error);

            return;
        }

        await base.OnResultExecutionAsync(context, next);
    }
}