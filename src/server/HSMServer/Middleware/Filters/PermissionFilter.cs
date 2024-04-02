using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Header = HSMSensorDataObjects.Header;

namespace HSMServer.Middleware;

public abstract class PermissionFilter(IPermissionService service, ITreeValuesCache cache, DataCollectorWrapper collector) : BaseKeyFilter(cache), IAsyncActionFilter, IAsyncResultFilter
{
    private const string ValuesArgument = "values";
    private const string CommandsArgument = "sensorCommands";


    protected abstract KeyPermissions Permissions { get; }


    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
         base.OnActionExecutionAsync(context.HttpContext);

        CheckPermission(context, RequestData, out _);
        await next();
    }
    
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Items.TryGetValue(TelemetryMiddleware.RequestData, out var value) && value is RequestData requestData)
            collector.Statistics[requestData.TelemetryPath]?.AddResponseResult(context.HttpContext.Response);

        await next();
    }

    private void CheckPermission(ActionExecutingContext context, RequestData requestData, out string message)
    {
        message = string.Empty;

        var values = context.ActionArguments.Values.FirstOrDefault();
        requestData.CollectorName = context.HttpContext.Request.Headers.TryGetValue(nameof(Header.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
        requestData.BuildTelemetryPath();

        if (values is BaseRequest request)
        {
            if (!service.CheckPermission(requestData, new SensorData(request), Permissions, out message))
            {
                context.HttpContext.Response.StatusCode = 406;
                return;
            }
        }

        switch (values)
        {
            case List<SensorValueBase> requestValues:
                AddRequestData<SensorValueBase>(context, requestData, values: requestValues, argumentName: ValuesArgument);
                break;
            case List<CommandRequestBase> commands:
                AddRequestData<CommandRequestBase>(context, requestData, values: commands, argumentName: CommandsArgument);
                break;
            default:
                AddRequestData<BaseRequest>(context, requestData);
                break;
        }
        
        collector.Statistics[requestData.TelemetryPath]?.AddRequestData(context.HttpContext.Request);
        collector.Statistics[requestData.TelemetryPath]?.AddReceiveData(requestData.Count);
    }
    
    
    private void AddRequestData<T>(ActionExecutingContext context, RequestData requestData, List<T> values = null, string argumentName = null) where T : BaseRequest
    {
        if (values is not null && !string.IsNullOrEmpty(argumentName))
        {
            context.ActionArguments.Remove(argumentName);
            values = values.Where(x => service.CheckPermission(requestData, new SensorData(x), Permissions, out _)).ToList();

            values.AddRange(service.GetPendingChecked<T>(requestData, Permissions));
            
            context.ActionArguments.Add(argumentName, values);

            requestData.Count = values.Count;
        }
    }

    public static ReadOnlySpan<string> GetPathParts(string path)
    {
        path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

        return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries).AsSpan();
    }
}