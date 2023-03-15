using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json;

namespace HSMDataCollector.Core
{
    internal sealed class HSMClient
    {
        internal readonly IDataQueue DataQueue;
        private readonly HttpClient _client;
        private readonly string _listSendingAddress;
        private readonly string _fileSendingAddress;
        private readonly LoggerManager _logManager;
        
        
        /// <summary>
        /// The event is fired after the values queue (current capacity is 100000 items) overflows
        /// </summary>
        [Obsolete]
        private event EventHandler ValuesQueueOverflow;
        
        
        internal HSMClient(CollectorOptions options, LoggerManager logger)
        {
            _logManager = logger;
            
            _listSendingAddress = options.ListEndpoint;
            _fileSendingAddress = options.FileEndpoint;
            
            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            };
            
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);
            
            DataQueue = new DataQueue(options);
            DataQueue.QueueOverflow += DataQueue_QueueOverflow;
            DataQueue.FileReceving += DataQueue_FileReceving;
            DataQueue.SendValues += DataQueue_SendValues;
        }

        internal void DisposeClient()
        {
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
                if (DataQueue != null && !DataQueue.Disposed)
                    DataQueue?.ReturnData(values);

                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }
        
        internal void DataQueue_FileReceving(object _, FileSensorValue value)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(value);

                if (_logManager.WriteDebug)
                    _logManager.Logger?.Debug($"{nameof(DataQueue_FileReceving)}: {jsonString}");

                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_fileSendingAddress, data).Result;

                if (!res.IsSuccessStatusCode)
                    _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                if (DataQueue != null && !DataQueue.Disposed)
                    DataQueue?.ReturnFile(value);

                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }
        
        internal void DataQueue_QueueOverflow(object sender, DateTime e)
        {
            OnValuesQueueOverflow();
        }
        
        internal void OnValuesQueueOverflow()
        {
            ValuesQueueOverflow?.Invoke(this, EventArgs.Empty);
        }
        
        internal void DataQueue_SendValues(object sender, List<SensorValueBase> e)
        {
            SendMonitoringData(e);
        }
    }
}