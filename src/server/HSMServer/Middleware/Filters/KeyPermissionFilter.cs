using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HSMServer.Middleware;

public class KeyPermissionFilter(ITreeValuesCache cache, IUpdatesQueue updatesQueue) : IAsyncActionFilter
{
    private const string ErrorTooLongPath = "Path for the sensor is too long.";
    private const string ErrorInvalidPath = "Path has an invalid format.";
    private const string ErrorPathKey = "Path or key is empty.";
    
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var values = context.ActionArguments.Values; // objects from argumaents: sensor list / value;

        var headers = context.HttpContext.Request.Headers;

        var value = values.FirstOrDefault() as SensorValueBase;

        if (value is null)
        {
            context.HttpContext.Response.StatusCode = 406;
            await next();
        }
        
        GetKey(value, headers, out var key);
        
        if (CanAddToQueue(key, value?.Path, out var message))
        {
            
        }
        
        await next();
    }
    
    private bool CanAddToQueue(Guid key, string path ,out string message)
    {
        return TryCheckRequest(key, path, out var pathParts, out message) &&
               cache.TryCheckKeyWritePermissions(key, pathParts, out message);

    }
    
    private static bool GetKey(BaseRequest request, IHeaderDictionary headers, out Guid guidKey)
    {
        headers.TryGetValue(nameof(BaseRequest.Key), out var key);

        if (string.IsNullOrEmpty(key))
            key = request?.Key;

        return Guid.TryParse(key, out guidKey);
    }
    
    public bool TryCheckRequest(Guid key, string path, out string[] pathParts, out string message)
    {
        pathParts = [];
        
        if (Guid.Empty == key || string.IsNullOrEmpty(path))
        {
            message = ErrorPathKey;
            return false;
        }

        pathParts = GetPathParts(path);
        
        if (pathParts.Contains(string.Empty) || path.Contains('\\') || path.Contains('\t'))
        {
            message = ErrorInvalidPath;
            return false;
        }
        
        if (pathParts.Length > ConfigurationConstants.DefaultMaxPathLength) // TODO : get maxPathLength from IConfigurationProvider
        {
            message = ErrorTooLongPath;
            return false;
        }

        message = string.Empty;
        return true;
    }
    
    private static string[] GetPathParts(string path)
    {
        path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

        return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries);
    }
}