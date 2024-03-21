using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.BackgroundServices;
using HSMServer.Core.Model;
using HSMServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Middleware;

public abstract class PermissionFilter(IPermissionService service, DataCollectorWrapper collector) : IAsyncActionFilter, IAsyncResultFilter
{
    protected abstract KeyPermissions Permissions { get; }


    public virtual void CheckPermission(ActionExecutingContext context, RequestData requestData, out string message)
    {
        message = string.Empty;

        var values = context.ActionArguments.Values.FirstOrDefault();
        var collectorName = context.HttpContext.Request.Headers.TryGetValue(nameof(Header.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
        requestData.TelemetryPath = $"{requestData.Product.DisplayName}/{requestData.Key.DisplayName}/{collectorName}";

        collector.Statistics[requestData.TelemetryPath].AddRequestData(context.HttpContext.Request);

        if (values is BaseRequest request)
        {
            if (!service.CheckPermission(requestData, new SensorData()
                {
                    Path = request.Path
                }, Permissions, out message))
            {
                context.HttpContext.Response.StatusCode = 406;
                return;
            }
        }


        if (values is List<SensorValueBase> requestValues)
        {
            context.ActionArguments.Remove("values");

            requestValues = requestValues.Where(x => service.CheckPermission(requestData, new SensorData() { Path = x.Path }, Permissions, out _)).ToList();

            context.ActionArguments.Add("values", requestValues);

            collector.Statistics[requestData.TelemetryPath].AddReceiveData(requestValues.Count);
            requestData.Count = requestValues.Count;
        }
        else if (values is List<CommandRequestBase> commands)
        {
            context.ActionArguments.Remove("sensorCommands");
            commands = commands.Where(x => service.CheckPermission(requestData, new SensorData() { Path = x.Path }, Permissions, out _)).ToList();

            context.ActionArguments.Add("sensorCommands", commands);

            collector.Statistics[requestData.TelemetryPath].AddReceiveData(commands.Count);
            requestData.Count = commands.Count;
        }
        else
        {
            collector.Statistics[requestData.TelemetryPath].AddReceiveData(1);
            requestData.Count = 1;
        }
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue(TelemetryMiddleware.RequestData, out var r) || r is not RequestData requestData)
            return;

        var headers = context.HttpContext.Request.Headers;
        var hasKey = GetKeyIdFromHeader(headers, out var keyId);

        if (!hasKey)
            return;

        var keyExist = service.TryGetKey(keyId, out var key, out var message);

        if (!keyExist)
            return;

        requestData.Key = key;

        var productExist = service.TryGetProduct(key.ProductId, out var product, out message);

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

    public static ReadOnlySpan<string> GetPathParts(string path)
    {
        path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

        return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries).AsSpan();
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Items.TryGetValue(TelemetryMiddleware.RequestData, out var value) && value is RequestData requestData)
            collector.Statistics[requestData.TelemetryPath].AddResponseResult(context.HttpContext.Response);
        
        await next();
    }
}
