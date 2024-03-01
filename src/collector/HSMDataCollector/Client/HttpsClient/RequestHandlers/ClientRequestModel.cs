using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;

namespace HSMDataCollector.Client.HttpsClient
{
    internal sealed class ClientRequestModel
    {
        internal StringContent Content { get; }

        internal string JsonMessage { get; }

        internal string Uri { get; }

        internal Guid Id { get; } = Guid.NewGuid();


        internal ClientRequestModel(object rawData, string uri)
        {
            Uri = uri;

            JsonMessage = JsonConvert.SerializeObject(rawData);
            Content = new StringContent(JsonMessage, Encoding.UTF8, "application/json");
        }
    }
}
