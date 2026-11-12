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
        /// True when the server answered and refused the package: a 4xx other than 408 and 429.
        /// The same payload and key never succeed on repeat, so a queue may treat this as final.
        /// A missing answer, a 5xx, a 408 (Request Timeout) and a 429 (Too Many Requests) are not
        /// rejections: they say "later", and the same package may be accepted then.
        /// </summary>
        internal bool IsRejected => StatusCode >= 400 && StatusCode < 500 && StatusCode != 408 && StatusCode != 429;


        public PackageSendingInfo(double contentSize, HttpResponseMessage response = null, string exception = null)
        {
            ContentSize = contentSize;

            IsSuccess = response?.IsSuccessStatusCode ?? false;
            StatusCode = response != null ? (int?)response.StatusCode : null;
            Error = !IsSuccess ? response == null ? exception : $"Code: {response.StatusCode}. {response.Content}" : null;
        }
    }
}