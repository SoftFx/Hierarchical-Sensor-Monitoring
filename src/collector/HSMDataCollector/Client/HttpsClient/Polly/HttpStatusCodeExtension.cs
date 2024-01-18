using System.Collections.Generic;
using System.Net;
using static System.Net.HttpStatusCode;

namespace HSMDataCollector.Client.HttpsClient.Polly
{
    public static class HttpStatusCodeExtension
    {
        private static readonly HashSet<int> _invalidCodes = new HashSet<int>()
        {
            (int)RequestTimeout,
            (int)Conflict,
            (int)Gone,
            421, // MisdirectedRequest
            423, // Locked
            429, // TooManyRequests
            (int)InternalServerError,
            (int)BadGateway,
            (int)ServiceUnavailable,
            (int)GatewayTimeout,
            506, // VariantAlsoNegotiates
            511 // NetworkAuthenticationRequired
        };


        public static bool IsRetryCode(this HttpStatusCode status) => _invalidCodes.Contains((int)status);
    }
}