using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HSMSensorDataObjects;
using HSMServer.Core.Cache;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware;

public abstract class BaseKeyFilter(ITreeValuesCache _cache)
{
    protected RequestData RequestData { get; set; }


    protected void OnActionExecutionAsync(HttpContext context)
    {
        if (!context.Items.TryGetValue(TelemetryMiddleware.RequestData, out var obj) || obj is not RequestData requestData)
            return;

        RequestData = GetInitializedRequest(requestData, context, _cache);
    }
    
    private static RequestData GetInitializedRequest(RequestData requestData, HttpContext context, ITreeValuesCache cache)
    {
        GetKeyIdFromHeader(context.Request.Headers, out var keyId);

        cache.TryGetKey(keyId, out var key, out _);
        requestData.Key = key;

        cache.TryGetProduct(key.ProductId, out var product, out _);
        requestData.Product = product;

        if (TryGetIp(context, out var ip))
            cache.SetLastKeyUsage(keyId, ip);
        
        return requestData;
        
        static bool GetKeyIdFromHeader(IHeaderDictionary headers, out Guid guidKey)
        {
            headers.TryGetValue(nameof(Header.Key), out var key);

            return Guid.TryParse(key, out guidKey);
        }
    }

    private static bool TryGetIp(HttpContext context, out string ip)
    {
        ip = context.Request.HttpContext.Connection.RemoteIpAddress?.ToString();

        if (ip is not null)
            return true;
        
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].AsReadOnly().FirstOrDefault();
        if (string.IsNullOrEmpty(forwardedFor)) 
            return false;
        
        foreach (ReadOnlySpan<char> i in forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries).AsReadOnly())
            if (IPAddress.TryParse(i.Trim(), out var address) && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            {
                ip = address.ToString();
                return ip is not null;
            }

        return false;
    }
}