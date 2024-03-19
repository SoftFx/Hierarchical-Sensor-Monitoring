using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Interfaces.Services;
using HSMServer.Core.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Middleware;

public abstract class PermissionFilter(ITreeValuesCache cache, DataCollectorWrapper collector) : IAsyncActionFilter, IAsyncResultFilter
{
    protected abstract KeyPermissions Permissions { get; }
    
    
    public virtual void CheckPermission(ActionExecutingContext context, FilterRequestData requestData, out string message)
    {
        message = string.Empty;
        
        var values = context.ActionArguments.Values.FirstOrDefault();
        var collectorName = context.HttpContext.Request.Headers.TryGetValue(nameof(Header.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
        requestData.TelemetryPath = $"{requestData.Product.DisplayName}/{requestData.Key.DisplayName}/{collectorName}";
        
        collector.Statistics[requestData.TelemetryPath].AddRequestData(context.HttpContext.Request);
        
        if (values is BaseRequest request && !cache.CheckPermission(requestData.Product, requestData.Key, GetPathParts(request?.Path), Permissions, out message))
        {
            context.HttpContext.Response.StatusCode = 406;
            return;
        }

        if (values is List<SensorValueBase> requestValues)
        {
            context.ActionArguments.Remove("values");
          
            requestValues = requestValues.Where(x => cache.CheckPermission(requestData.Product, requestData.Key, GetPathParts(x?.Path), Permissions, out _)).ToList();
            
            context.ActionArguments.Add("values", requestValues);
            
            collector.Statistics[requestData.TelemetryPath].AddReceiveData(requestValues.Count);
            context.HttpContext.Items["SensorsCount"] = requestValues.Count;
        }
        else
        {
            collector.Statistics[requestData.TelemetryPath].AddReceiveData(1);
            context.HttpContext.Items["SensorsCount"] = 1;
        }
    }
    
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(TelemetryMiddleware.RequestData, out var r) || r is not FilterRequestData requestData)
            return;
        
        var headers = context.HttpContext.Request.Headers;
        var hasKey = GetKeyIdFromHeader(headers, out var keyId);

        if (!hasKey)
            return;

        var keyExist = cache.TryGetKey(keyId, out var key, out var message);

        if (!keyExist)
            return;
        
        requestData.Key = key;

        var productExist = cache.TryGetProduct(key.ProductId, out var product, out message);

        if (!productExist)
            return;
        
        requestData.Product = product;

        CheckPermission(context, requestData, out message);
        await next();
    }
    

    private static bool GetKeyIdFromHeader(IHeaderDictionary headers, out Guid guidKey)
    {
        headers.TryGetValue(nameof(Header.Key), out var key);

        return Guid.TryParse(key, out guidKey);
    }

    private static ReadOnlySpan<string> GetPathParts(string path)
    {
        path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

        return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries).AsSpan();
    }
    
    
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        collector.Statistics[context.HttpContext.Request.Headers["Path"]].AddResponseResult(context.HttpContext.Response);
        await next();
    }
}
