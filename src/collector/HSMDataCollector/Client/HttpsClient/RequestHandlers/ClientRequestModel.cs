using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;

namespace HSMDataCollector.Client.HttpsClient
{
    internal sealed class ClientRequestModel
    {
        internal string JsonMessage { get; }

        internal string Uri { get; }

        internal Guid Id { get; } = Guid.NewGuid();


        internal ClientRequestModel(object rawData, string uri)
        {
            JsonMessage = JsonConvert.SerializeObject(rawData);
            Uri = uri;
        }


        internal StringContent GetContent() => new StringContent(JsonMessage, Encoding.UTF8, "application/json");
    }
}
