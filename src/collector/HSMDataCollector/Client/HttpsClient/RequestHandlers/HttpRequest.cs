using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;


namespace HSMDataCollector.Client.HttpsClient
{
    internal readonly struct HttpRequest<T>
    {
        internal string JsonMessage { get; }

        internal string Uri { get; }

        internal int Length => JsonMessage?.Length ?? 0;

        public HttpRequest(IEnumerable<T> values, string uri)
        {
            Uri = uri;
            JsonMessage = JsonConvert.SerializeObject(values);
        }

        public HttpRequest(T value, string uri)
        {
            Uri = uri;
            JsonMessage = JsonConvert.SerializeObject(value);
        }

        internal StringContent GetContent() => new StringContent(JsonMessage, Encoding.UTF8, "application/json");
    }
}
