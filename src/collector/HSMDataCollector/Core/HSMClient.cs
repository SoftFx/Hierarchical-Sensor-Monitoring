using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json;

namespace HSMDataCollector.Core
{
    internal sealed class HSMClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;
        private readonly LoggerManager _logManager;
        private readonly string _listSendingAddress;
        private readonly string _fileSendingAddress;
        
        
        internal HSMClient(CollectorOptions options, IDataQueue dataQueue, LoggerManager logger)
        {
            _dataQueue = dataQueue;
            
            _logManager = logger;
            
            _listSendingAddress = options.ListEndpoint;
            _fileSendingAddress = options.FileEndpoint;

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            };
            
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);
            
            _dataQueue.FileReceiving += DataQueueFileReceiving;
            _dataQueue.SendValues += SendMonitoringData;
        }

        public void Dispose()
        {
            _dataQueue.FileReceiving -= DataQueueFileReceiving;
            _dataQueue.SendValues -= SendMonitoringData;
            
            _client.Dispose();
        }
        
        internal void SendMonitoringData(List<SensorValueBase> values)
        {
            try
            {
                if (values.Count == 0)
                    return;

                string jsonString = JsonConvert.SerializeObject(values.Cast<object>());

                if (_logManager.WriteDebug)
                    _logManager.Logger?.Debug($"{nameof(SendMonitoringData)}: {jsonString}");

                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_listSendingAddress, data).Result;

                if (!res.IsSuccessStatusCode)
                    _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                    _dataQueue?.ReturnData(values);

                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }
        
        internal void DataQueueFileReceiving(FileSensorValue value)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(value);

                if (_logManager.WriteDebug)
                    _logManager.Logger?.Debug($"{nameof(DataQueueFileReceiving)}: {jsonString}");

                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_fileSendingAddress, data).Result;

                if (!res.IsSuccessStatusCode)
                    _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                    _dataQueue?.ReturnFile(value);

                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }
    }
}