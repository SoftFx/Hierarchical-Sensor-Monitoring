using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Http;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    public class TelemetryCollector(DataCollectorWrapper _collector, ITreeValuesCache _cache)
    {
        private const string ClientNameHeader = nameof(Header.ClientName);
        private const string AccessKeyHeader = nameof(Header.Key);

        private const string XForvardHeader = "X-Forwarded-For"; // real ip without vpn redirection
        private const string EmptyClient = "No name";

        private protected readonly ClientStatisticsSensors _statistics = _collector.WebRequestsSensors;


        public async Task<bool> TryRegisterPublicApiRequest(HttpContext context)
        {
            if (IsPublicApiRequest(context))
            {
                _statistics.Total.AddRequestData(context.Request);

                if (TryBuildPublicApiInfo(context, out var info, out var error))
                {
                    context.SetPublicApiInfo(info);

                    _statistics[info.TelemetryPath].AddRequestData(context.Request);
                }
                else
                {
                    await context.SetAccessError(error);

                    return false;
                }
            }

            return true;
        }

        [Obsolete("Should be removed ater migration from v3 to v4")]
        public static bool TryAddKeyToHeader(HttpContext context, string accessKey) =>
            context.TryWriteInfo(AccessKeyHeader, accessKey);


        private static bool IsPublicApiRequest(HttpContext context) => context.TryReadInfo(AccessKeyHeader, out _);

        private bool TryBuildPublicApiInfo(HttpContext context, out PublicApiRequestInfo info, out string error)
        {
            info = null;
            error = null;

            if (!TryGetApiKey(context, out var apiKeyId))
                return false;

            if (!_cache.TryGetKey(apiKeyId, out var apiKey, out error))
                return false;

            if (!_cache.TryGetProduct(apiKey.ProductId, out var product, out error))
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
            }.Init();

            return true;
        }

        private static bool TryGetApiKey(HttpContext context, out Guid apiKey)
        {
            apiKey = Guid.Empty;

            return context.TryReadInfo(AccessKeyHeader, out var key) && !string.IsNullOrEmpty(key) && Guid.TryParse(key, out apiKey);
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
