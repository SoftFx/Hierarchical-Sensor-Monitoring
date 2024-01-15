using System.Collections.Generic;
using System.Net;
using static System.Net.HttpStatusCode;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public static class HttpStatusCodeExtension
    {
        private static readonly HashSet<HttpStatusCode> _invalidCodes = new HashSet<HttpStatusCode>()
        {
            RequestTimeout,
            Conflict,
            Gone,
            InternalServerError,
            BadGateway,
            GatewayTimeout,
        };


        public static bool CheckForCodeToRetry(this HttpStatusCode status) => _invalidCodes.Contains(status);
    }
}