using HSMDataCollector.Converters;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;


namespace HSMDataCollector.Client.HttpsClient
{
    internal readonly struct HttpRequest<T>
    {
        internal string Content { get; }

        internal string Uri { get; }

        internal int Length => Content?.Length ?? 0;

        private static JsonSerializerOptions _options = new JsonSerializerOptions 
        {
            Converters = 
            {
                new JsonSensorConverter(),
                new JsonCommandConverter(),
            }
        };

        public HttpRequest(IEnumerable<T> values, string uri)
        {
            Uri = uri;
            Content = JsonSerializer.Serialize(values, _options);
        }

        public HttpRequest(T value, string uri)
        {
            Uri = uri;
            Content = JsonSerializer.Serialize(value, _options);
        }

        internal StringContent GetContent() => new StringContent(Content, Encoding.UTF8, "application/json");
    }
}
