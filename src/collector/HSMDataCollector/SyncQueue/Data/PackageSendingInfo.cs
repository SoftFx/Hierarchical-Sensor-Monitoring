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
            Settled = null;
            DroppedCount = 0;
            IsTransportDown = false;
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
            Settled = null;
            DroppedCount = 0;
            IsTransportDown = false;
        }

        /// <summary>
        /// For a failed send: which items of the package, by position, are settled all the same
        /// (delivered, refused for good, or dropped and counted). The queue takes back only the
        /// others. Null when none is. Set by a sender that delivers a package in parts.
        /// </summary>
        internal bool[] Settled { get; }

        /// <summary>Items the sender dropped because they can never be sent; counted as overflow.</summary>
        internal int DroppedCount { get; }

        /// <summary>
        /// The failure is a state of the transport that the transport reports itself (no
        /// session). The queue keeps the package and does not write an error per cycle about it.
        /// </summary>
        internal bool IsTransportDown { get; }

        private PackageSendingInfo(double contentSize, bool isSuccess, int? statusCode, string error, bool[] settled, int droppedCount, bool isTransportDown)
        {
            ContentSize = contentSize;
            IsSuccess = isSuccess;
            StatusCode = statusCode;
            Error = error;
            Settled = settled;
            DroppedCount = droppedCount;
            IsTransportDown = isTransportDown;
        }

        /// <summary>Nothing reached the server, or no answer came back: there is no status.</summary>
        internal static PackageSendingInfo NotSent(string reason, bool[] settled = null, int droppedCount = 0) =>
            new PackageSendingInfo(0, false, null, reason ?? "not sent", settled, droppedCount, isTransportDown: true);

        /// <summary>The server answered the whole package with one status.</summary>
        internal static PackageSendingInfo Answered(double contentSize, int statusCode, string error, int droppedCount)
        {
            var isSuccess = statusCode >= 200 && statusCode < 300;

            return new PackageSendingInfo(contentSize, isSuccess, statusCode, isSuccess ? null : error ?? $"Code: {statusCode}.", null, droppedCount, false);
        }

        /// <summary>The server refused a part of the package; <paramref name="settled"/> marks what is done with all the same.</summary>
        internal static PackageSendingInfo Failed(double contentSize, int statusCode, string error, bool[] settled, int droppedCount) =>
            new PackageSendingInfo(contentSize, false, statusCode, error ?? $"Code: {statusCode}.", settled, droppedCount, false);

        /// <summary>
        /// The text of a refused request: the status, its reason phrase and, when the caller
        /// has read one, the server's own message. Never the content object itself: that
        /// printed a type name (#1659 A1).
        /// </summary>
        internal static string DescribeFailure(HttpResponseMessage response, string serverMessage)
        {
            var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;

            // The reason phrase is the server's (or a proxy's) text too.
            if (reason.Length > MaxDescriptionPartLength)
                reason = reason.Substring(0, MaxDescriptionPartLength) + "...";

            var text = $"Code: {(int)response.StatusCode} ({reason}).";

            return string.IsNullOrEmpty(serverMessage) ? text : $"{text} {serverMessage}";
        }

        /// <summary>
        /// The longest piece of text taken from a server's answer into a failure description.
        /// </summary>
        internal const int MaxDescriptionPartLength = 256;

        /// <summary>
        /// The text of a send that got no answer. The message of an <c>HttpRequestException</c>
        /// only says that the request failed; the cause (name not resolved, connection refused,
        /// certificate not trusted) is in the innermost exception, so both are kept.
        /// </summary>
        internal static string DescribeException(System.Exception ex)
        {
            var cause = ex.GetBaseException();

            return ReferenceEquals(cause, ex) || string.IsNullOrEmpty(cause.Message) || ex.Message.Contains(cause.Message)
                ? ex.Message
                : $"{ex.Message} {cause.Message}";
        }
    }
}