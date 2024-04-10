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
        private const string ClientNameHeader = nameof(Header.ClientName);
        private const string AccessKeyHeader = nameof(Header.Key);

        private const string XForvardHeader = "X-Forwarded-For"; // real ip without vpn redirection
        private const string EmptyClient = "No name";


        public async Task InvokeAsync(HttpContext context)
        {
            if (IsPublicApiRequest(context))
            {
                _collector.WebRequestsSensors.Total.AddRequestData(context.Request);

                if (TryBuildPublicApiInfo(context, out var info, out var error))
                {
                    context.SetPublicApiInfo(info);

                    _collector.WebRequestsSensors[info.TelemetryPath]?.AddRequestData(context.Request);
                    //_collector.WebRequestsSensors[info.TelemetryPath]?.AddReceiveData(requestData.Count);
                }
                else
                {
                    await context.SetAccessError(error);

                    return;
                }
            }

            await _next(context);

            if (context.TryGetPublicApiInfo(out var requestInfo))
            {
                _collector.WebRequestsSensors.Total.AddResponseResult(context.Response);

                //_collector.WebRequestsSensors.Total.AddReceiveData(requestData.Count);
                _collector.WebRequestsSensors[requestInfo.TelemetryPath]?.AddResponseResult(context.Response);
            }
        }


        private static bool IsPublicApiRequest(HttpContext context) => context.TryReadInfo(AccessKeyHeader, out _);

        private bool TryBuildPublicApiInfo(HttpContext context, out PublicApiRequestInfo info, out string error)
        {
            info = null;
            error = null;

            if (!TryGetApiKey(context, out var apiKeyId))
                return false;

            if (_cache.TryGetKey(apiKeyId, out var apiKey, out error))
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

            return true; //add build telemetry properties
        }

        private static bool TryGetApiKey(HttpContext context, out Guid apiKey)
        {
            apiKey = Guid.Empty;

            return context.TryReadInfo(AccessKeyHeader, out var key) && Guid.TryParse(key.ToString(), out apiKey);
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

        private static string GetClientName(HttpContext context) => context.TryReadInfo(ClientNameHeader, out var name) && !string.IsNullOrWhiteSpace(name) ? name.ToString() : EmptyClient;
    }
}