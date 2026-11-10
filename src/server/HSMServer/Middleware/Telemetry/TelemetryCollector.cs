using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Http;
using NLog;
using System;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    public class TelemetryCollector(DataCollectorWrapper _collector, ITreeValuesCache _cache)
    {
        private const string ClientNameHeader = nameof(Header.ClientName);
        private const string AccessKeyHeader = nameof(Header.Key);

        private const string EmptyClient = "No name";

        private protected readonly ClientStatisticsSensors _statistics = _collector?.WebRequestsSensors;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Resolve and register the request's public-API info (key, product,
        /// telemetry counters). Answers TRUE both for a registered request
        /// and for a request that is not public-API at all (no Key header —
        /// nothing to register, the pipeline continues untouched); a
        /// rejected key answers FALSE with its reason and writes NOTHING to
        /// the response — the two callers answer differently: the middleware
        /// writes the 406 body directly (before MVC runs, safe), an action
        /// returns the error as its OWN result so MVC formats it once. A
        /// hand-written body from inside an action started the response and
        /// MVC's result formatting then threw over it — three ERROR entries
        /// and a truncated body per rejected request (#1649). Virtual for
        /// the middleware tests (#1649 review: the not-a-key-request arm
        /// answering false would cut the whole web UI off).
        /// </summary>
        public virtual bool TryRegisterPublicApiRequest(HttpContext context, out string error)
        {
            if (!IsPublicApiRequest(context))
            {
                error = string.Empty;

                return true;
            }

            error = string.Empty;

            try
            {
                _statistics.Total.AddRequestData(context.Request);

                if (TryBuildPublicApiInfo(context, out var info, out var rejectReason))
                {
                    context.SetPublicApiInfo(info);

                    _statistics[info.TelemetryPath].AddRequestData(context.Request);

                    return true;
                }

                // A rejected key is a client mistake, not a server fault:
                // WARN without a stack trace (the ADR-0016 operator-surface
                // contract) — the reason reaches the caller, not the log.
                error = rejectReason;
                _logger.Warn($"Rejected {context.Request.Method} {context.Request.Path} request: {error}");

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
                error = "Cannot register the request";

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

            if (!TryGetRemoteIP(context, out var remoteIp))
            {
                error = "Cannot determine the client address";
                return false;
            }

            _cache.SetLastKeyUsage(apiKeyId, remoteIp);

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
        

        // The connection address only. Behind the bundled proxy, UseForwardedHeaders has
        // already replaced it with the client's, from trusted proxies only (#1427); reading
        // X-Forwarded-For here would trust whatever entries a client supplied.
        private static bool TryGetRemoteIP(HttpContext context, out string remoteIp)
        {
            var address = context.Connection.RemoteIpAddress;

            if (address is null)
            {
                remoteIp = null;
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            remoteIp = address.ToString();
            return true;
        }

        private static string GetClientName(HttpContext context) => context.TryReadInfo(ClientNameHeader, out var name) && !string.IsNullOrWhiteSpace(name) ? name : EmptyClient;
    }
}
