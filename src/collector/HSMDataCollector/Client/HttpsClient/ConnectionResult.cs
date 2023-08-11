using System.Net;

namespace HSMDataCollector.Core
{
    public readonly struct ConnectionResult
    {
        public static ConnectionResult Ok { get; } = new ConnectionResult();


        public HttpStatusCode? Code { get; }

        public string Error { get; }


        public bool Result => string.IsNullOrEmpty(Error);


        public ConnectionResult(HttpStatusCode? code, string error)
        {
            Error = error;
            Code = code;
        }
    }
}
