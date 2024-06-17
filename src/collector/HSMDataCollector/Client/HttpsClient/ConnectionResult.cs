using System.Net;

namespace HSMDataCollector.Core
{
    public readonly struct ConnectionResult
    {
        public static ConnectionResult Ok { get; } = new ConnectionResult(HttpStatusCode.OK);

        public HttpStatusCode? Code { get; }

        public string Error { get; }

        public bool Result => string.IsNullOrEmpty(Error);

        public bool IsOk => Code == HttpStatusCode.OK;


        public ConnectionResult(HttpStatusCode? code, string error = null)
        {
            Error = error;
            Code = code;
        }
    }
}