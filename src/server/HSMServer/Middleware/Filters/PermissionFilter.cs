using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Middleware;

public class PermissionFilter<T>(IPermissionService service, DataCollectorWrapper collector) : IAsyncActionFilter where T : BaseRequest
{
    private const string ErrorTooLongPath = "Path for the sensor is too long.";
    private const string ErrorInvalidPath = "Path has an invalid format.";
    private const string ErrorPathKey = "Path or key is empty.";
    
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var values = context.ActionArguments.Values.FirstOrDefault(); // objects from argumaents: sensor list / value;

        var headers = context.HttpContext.Request.Headers;
        
        if (values is T request)
        {
            if (GetKeyIdFromHeader(request, headers, out var keyId))
            {
                if (service.TryGetKey(keyId, out var key, out var message))
                {
                    if (service.TryGetProduct(key.ProductId, out var product, out message))
                    {
                        var collectorName = context.HttpContext.Request.Headers.TryGetValue(nameof(BaseRequest.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
                        var path = $"{product.DisplayName}/{key.DisplayName}/{collectorName}";
                        context.HttpContext.Request.Headers["Path"] = path;
                        collector.Statistics[path].AddRequestData(context.HttpContext.Request);

                        if (service.CheckWritePermissions(product, key, GetPathParts(request?.Path), out message))
                        {
                            await next();
                        }
                        else
                        {
                            context.HttpContext.Response.StatusCode = 406;
                            return;
                        }
                    }
                }
            }

        }

        if (values is List<T> tValues)
        {
            //should be handled for T that will be specified in action
        }
        
        await next();
    }
    
    
    private static bool GetKeyIdFromHeader(BaseRequest request, IHeaderDictionary headers, out Guid guidKey)
    {
        headers.TryGetValue(nameof(BaseRequest.Key), out var key);

        if (string.IsNullOrEmpty(key))
            key = request?.Key;

        return Guid.TryParse(key, out guidKey);
    }
    
    private static string[] GetPathParts(string path)
    {
        path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

        return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries);
    }
}