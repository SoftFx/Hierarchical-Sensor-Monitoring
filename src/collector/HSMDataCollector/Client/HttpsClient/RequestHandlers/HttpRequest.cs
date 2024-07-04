using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HSMDataCollector.Converters;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


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
                new JsonRequestConverter<SensorValueBase>(),
                new JsonRequestConverter<CommandRequestBase>(),
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
