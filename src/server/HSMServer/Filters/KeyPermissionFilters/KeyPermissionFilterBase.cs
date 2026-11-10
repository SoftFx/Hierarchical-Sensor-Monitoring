using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
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
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


    public override Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // A key without the needed permission answers 406 through the RESULT,
        // not a hand-written response: writing the body here started the
        // response, and whatever MVC ran next threw over it (#1649). The
        // plain-text body matches what SetAccessError used to write. The
        // rejection logs one WARN without a stack trace, like the key-state
        // rejections in TelemetryCollector (the ADR-0016 operator surface).
        if (context.HttpContext.TryGetPublicApiInfo(out var info) && !info.Key.IsValid(_permissions, out var error))
        {
            _logger.Warn(
                $"Rejected {context.HttpContext.Request.Method} {context.HttpContext.Request.Path} request: {error}");

            context.Result = new ContentResult { StatusCode = 406, Content = error };
        }

        return next();
    }
}
