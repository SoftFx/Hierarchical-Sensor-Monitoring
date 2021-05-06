using HSMCommon.Model.SensorsData;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace HSMWebClient
{
    public static class ApiConnector
    {
        private static HttpClient _client;
        private static int _timeout = 15;

        static ApiConnector()
        {           
            HttpClientHandler httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback =
                (sender, cert, chain, sslPolicyErrors) => true;

            _client = new HttpClient(httpClientHandler);
            _client.Timeout = TimeSpan.FromSeconds(_timeout);
        }

        public static List<SensorData> GetTree(string address, int port)
        {
            var result = GetResponse(address, port, TextConstants.GetTree);
            return JsonConvert.DeserializeObject<List<SensorData>>(result);
        }

        private static string GetResponse(string address, int port, string method)
        {
            string result = string.Empty;

            try
            {
                var task = _client.GetStringAsync($"{address}:{port}/{TextConstants.Api}/{method}");
                task.Wait();
                result = task.Result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to server {address} !");
            }

            return result;
        }
    }
}
