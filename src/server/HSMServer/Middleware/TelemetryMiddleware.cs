using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Http;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public sealed class TelemetryMiddleware(RequestDelegate _next, DataCollectorWrapper _collector, ITreeValuesCache _cache)
    {
        private const string XForvardHeader = "X-Forwarded-For"; // real ip without vpn redirection
        private const string EmptyClient = "No name";

        public const string RequestData = "RequestData";

        //private readonly IUserManager _userManager = userManager;
        //private readonly RequestDelegate _next = next;


        public async Task InvokeAsync(HttpContext context)
        {
            var isApiRequest = IsPublicApiRequest(context, out var info);

            if (isApiRequest)
            {
                context.SetPublicApiInfo(info);

                _collector.WebRequestsSensors[info.TelemetryPath]?.AddRequestData(context.Request);
                //_collector.WebRequestsSensors[info.TelemetryPath]?.AddReceiveData(requestData.Count);
            }

            _collector.WebRequestsSensors.Total.AddRequestData(context.Request);

            await _next(context);

            _collector.WebRequestsSensors.Total.AddResponseResult(context.Response);

            if (isApiRequest)
            {
                //_collector.WebRequestsSensors.Total.AddReceiveData(requestData.Count);
                _collector.WebRequestsSensors[info.TelemetryPath]?.AddResponseResult(context.Response);
            }
        }


        private bool IsPublicApiRequest(HttpContext context, out PublicApiRequestInfo info)
        {
            info = null;

            if (!TryGetApiKey(context, out var apiKeyId))
                return false;

            if (_cache.TryGetKey(apiKeyId, out var apiKey, out var error))
                return false;

            if (_cache.TryGetProduct(apiKey.ProductId, out var product, out error))
                return false;

            if (TryGetRemoteIP(context, out var remoteIp))
                _cache.SetLastKeyUsage(apiKeyId, remoteIp);
            else
                return false;

            info = new PublicApiRequestInfo()
            {
                Product = product,
                Key = apiKey,
                RemoteIP = remoteIp,
                CollectorName = GetClientName(context),
            };

            return true;
        }

        private static bool TryGetApiKey(HttpContext context, out Guid apiKey)
        {
            apiKey = Guid.Empty;

            return context.TryReadInfo(nameof(Header.Key), out var key) && Guid.TryParse(key.ToString(), out apiKey);
        }

        private static bool TryGetRemoteIP(HttpContext context, out string remoteIp)
        {
            remoteIp = context.Request.HttpContext.Connection.RemoteIpAddress?.ToString();

            if (remoteIp is null && context.TryReadInfo(XForvardHeader, out var forwardFor) && !string.IsNullOrEmpty(forwardFor))
            {
                foreach (var ipAddressRaw in forwardFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (IPAddress.TryParse(ipAddressRaw, out var address) && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    {
                        remoteIp = address.ToString();
                        break;
                    }
            }

            return remoteIp is not null;
        }

        private static string GetClientName(HttpContext context) => context.TryReadInfo(nameof(Header.ClientName), out var name) && !string.IsNullOrWhiteSpace(name) ? name.ToString() : EmptyClient;
    }
}