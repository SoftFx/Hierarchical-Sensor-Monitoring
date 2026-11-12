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
        /// True when the server answered and refused the package (4xx). The same payload and
        /// key never succeed on repeat, so a queue may treat this as final. A missing answer
        /// and a 5xx are not rejections: the server may accept the same package later.
        /// The boundary is the one the native collector draws (no answer or 5xx: retried).
        /// </summary>
        internal bool IsRejected => StatusCode >= 400 && StatusCode < 500;


        public PackageSendingInfo(double contentSize, HttpResponseMessage response = null, string exception = null)
        {
            ContentSize = contentSize;

            IsSuccess = response?.IsSuccessStatusCode ?? false;
            StatusCode = response != null ? (int?)response.StatusCode : null;
            Error = !IsSuccess ? response == null ? exception : $"Code: {response.StatusCode}. {response.Content}" : null;
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
    }
}