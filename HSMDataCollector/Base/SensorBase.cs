using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.Base
{
    public abstract class SensorBase
    {
        protected readonly string Path;
        protected readonly string ProductKey;
        protected readonly string ServerAddress;
        private int _count = 0;
        private readonly HttpClient _client;
        private readonly object _syncRoot = new object();
        protected SensorBase(string path, string productKey, string serverAddress, HttpClient client)
        {
            _client = client;
            Path = path;
            ProductKey = productKey;
            ServerAddress = serverAddress;
        }

        protected abstract byte[] GetBytesData(SensorValueBase data);
        protected abstract string GetStringData(SensorValueBase data);
        protected void SendData(string serializedValue)
        {
            try
            {
                Console.WriteLine($"Sending data for {Path} at {DateTime.Now:G}");
                var data = new StringContent(serializedValue, Encoding.UTF8, "application/json");
                _client.PostAsync(ServerAddress, data);
                //var request = WebRequest.Create(ServerAddress);
                //request.Method = "POST";
                //request.ContentType = "application/json";
                //byte[] bytesData = Encoding.UTF8.GetBytes(serializedValue);
                //using (var stream = request.GetRequestStream())
                //{
                //    stream.Write(bytesData, 0, bytesData.Length);
                //}
                //var response = request.GetResponseAsync();
                //response.Wait();
                Console.WriteLine("Data sent");
            }
            catch (Exception e)
            {
                //TODO: enqueue the value
                Console.WriteLine(e);
            }
        }
    }
}