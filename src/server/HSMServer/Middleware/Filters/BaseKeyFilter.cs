using System;
using HSMSensorDataObjects;
using HSMServer.Core.Cache;
using HSMServer.Core.TreeStateSnapshot;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware;

public abstract class BaseKeyFilter(ITreeValuesCache cache)
{
    protected RequestData RequestData { get; set; }


    protected void OnActionExecutionAsync(HttpContext context)
    {
        if (!context.Items.TryGetValue(TelemetryMiddleware.RequestData, out var obj) || obj is not RequestData requestData)
            return;

        RequestData = GetInitializedRequest(requestData, context, cache);
    }
    
    private static RequestData GetInitializedRequest(RequestData requestData, HttpContext context, ITreeValuesCache cache)
    {
        GetKeyIdFromHeader(context.Request.Headers, out var keyId);

        cache.TryGetKey(keyId, out var key, out var message);
        requestData.Key = key;
        
        cache.TryGetProduct(key.ProductId, out var product, out message);
        requestData.Product = product;

        cache.UpdateKeyUseState(requestData.Key, context.Request.HttpContext.Connection.RemoteIpAddress);
        
        return requestData;
        
        static bool GetKeyIdFromHeader(IHeaderDictionary headers, out Guid guidKey)
        {
            headers.TryGetValue(nameof(Header.Key), out var key);

            return Guid.TryParse(key, out guidKey);
        }
    }
}