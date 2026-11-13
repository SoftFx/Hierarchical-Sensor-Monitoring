using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
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

        /// <summary>
        /// One element of a batch, written exactly as the batch constructor above writes it
        /// inside its array. The collector session builds its frames from these, so a frame body
        /// is the HTTP body byte for byte.
        /// </summary>
        internal static string SerializeElement(T value) => JsonSerializer.Serialize(value, _options);

        internal StringContent GetContent() => new StringContent(Content, Encoding.UTF8, "application/json");
    }
}
