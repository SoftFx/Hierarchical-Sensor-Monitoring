using System.Net.Http;

namespace HSMDataCollector.SyncQueue.Data
{
    public readonly struct PackageSendingInfo
    {
        public double ContentSize { get; }

        public bool IsSuccess { get; }

        public string Error { get; }

        /// <summary>
        /// The status the server answered with, or <c>null</c> when no answer arrived
        /// (the connection failed, the request timed out or was cancelled).
        /// </summary>
        public int? StatusCode { get; }

        /// <summary>
        /// True when the server answered and refused the package: a 4xx, or any other answer
        /// below 500 that is not a success (a redirect the client could not follow). The same
        /// payload and key never succeed on repeat, so a queue may treat this as final. A
        /// missing answer and a 5xx are not rejections: the server may accept the same package
        /// later. The boundary is the one the native collector draws: no answer or a 5xx is
        /// retried, every other answer that is not a success is final.
        /// </summary>
        internal bool IsRejected => !IsSuccess && StatusCode > 0 && StatusCode < 500;


        public PackageSendingInfo(double contentSize, HttpResponseMessage response = null, string exception = null)
        {
            ContentSize = contentSize;

            IsSuccess = response?.IsSuccessStatusCode ?? false;
            StatusCode = response != null ? (int?)response.StatusCode : null;
            Error = !IsSuccess ? response == null ? exception : DescribeFailure(response, serverMessage: null) : null;
        }

        /// <summary>
        /// For a sender that has the answered status but no <see cref="HttpResponseMessage"/>
        /// (a transport other than HTTP). A status outside 200..299 is a failed send;
        /// <paramref name="error"/> describes it.
        /// </summary>
        public PackageSendingInfo(double contentSize, int statusCode, string error)
        {
            ContentSize = contentSize;

            IsSuccess = statusCode >= 200 && statusCode < 300;
            StatusCode = statusCode;
            Error = IsSuccess ? null : error ?? $"Code: {statusCode}.";
        }

        /// <summary>
        /// The text of a refused request: the status, its reason phrase and, when the caller
        /// has read one, the server's own message. Never the content object itself: that
        /// printed a type name (#1659 A1).
        /// </summary>
        internal static string DescribeFailure(HttpResponseMessage response, string serverMessage)
        {
            var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
            var text = $"Code: {(int)response.StatusCode} ({reason}).";

            return string.IsNullOrEmpty(serverMessage) ? text : $"{text} {serverMessage}";
        }
    }
}