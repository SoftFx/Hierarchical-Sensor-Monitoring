using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Http;
using NLog;
using System;
using System.Net;
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

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public async Task<bool> TryRegisterPublicApiRequest(HttpContext context)
        {
            try
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
            catch (Exception ex) 
            {
                _logger.Error(ex);
                return false;
            }
        }

        [Obsolete("Should be removed ater migration from v3 to v4")]
        public static bool TryAddKeyToHeader(HttpContext context, string accessKey) =>
            context.TryWriteInfo(AccessKeyHeader, accessKey);


        private static bool IsPublicApiRequest(HttpContext context) => context.TryReadInfo(AccessKeyHeader, out _);

        private bool TryBuildPublicApiInfo(HttpContext context, out PublicApiRequestInfo info, out string error)
        {
            info = null;
            error = string.Empty;

            if (!TryGetApiKey(context, out var apiKeyId, out error))
                return false;

            if (!_cache.TryGetKey(apiKeyId, out var apiKey, out error))
                return false;

            if (!_cache.TryGetRootProduct(apiKey.ProductId, out var product, out error))
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

        private static bool TryGetApiKey(HttpContext context, out Guid apiKey, out string error)
        {
            apiKey = Guid.Empty;

            var result = context.TryReadInfo(AccessKeyHeader, out var key) && !string.IsNullOrEmpty(key) && Guid.TryParse(key, out apiKey);
            error = result ? string.Empty : "Invalid access key";

            return result;
        }
        

        private static bool TryGetRemoteIP(HttpContext context, out string remoteIp)
        {
            if (TryMapIPToString(context.Request.HttpContext.Connection.RemoteIpAddress, out remoteIp))
                return remoteIp is not null;

            if (remoteIp is null && context.TryReadInfo(XForvardHeader, out var forwardFor) && !string.IsNullOrEmpty(forwardFor))
                foreach (var ipAddressRaw in forwardFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (IPAddress.TryParse(ipAddressRaw, out var address) && TryMapIPToString(address, out remoteIp))
                        break;

            return remoteIp is not null;
        }

        private static bool TryMapIPToString(IPAddress address, out string ip)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            ip = address.ToString();

            return ip is not null;
        }

        private static string GetClientName(HttpContext context) => context.TryReadInfo(ClientNameHeader, out var name) && !string.IsNullOrWhiteSpace(name) ? name : EmptyClient;
    }
}
