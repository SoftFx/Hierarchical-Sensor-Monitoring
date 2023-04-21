using HSMDataCollector.Logging;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    internal sealed class HSMClient : IDisposable
    {
        private readonly LoggerManager _logManager;
        private readonly IDataQueue _dataQueue;
        private readonly HttpClient _client;

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

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);

            _dataQueue.SendValueHandler += DataQueueFileReceiving;
            _dataQueue.SendValuesHandler += SendData;
        }

        internal async Task SendFileAsync(FileInfo fileInfo, string sensorPath, SensorStatus sensorStatus = SensorStatus.Ok, string comment = "")
        {
            async Task<List<byte>> GetFileBytes()
            {
                using (var stream = new StreamReader(fileInfo.FullName))
                    return Encoding.UTF8.GetBytes(await stream.ReadToEndAsync()).ToList();
            }

            var value = new FileSensorValue()
            {
                Path = sensorPath,
                Comment = comment,
                Status = sensorStatus,
                Extension = fileInfo.Extension.TrimStart('.'),
                Name = Path.GetFileNameWithoutExtension(fileInfo.FullName),
                Time = DateTime.Now,
                Value = await GetFileBytes()
            };

            await DataQueueFileReceivingAsync(value);
        }


        public void Dispose()
        {
            _dataQueue.SendValueHandler -= DataQueueFileReceiving;
            _dataQueue.SendValuesHandler -= SendData;

            _client.Dispose();
        }


        internal void SendData(List<SensorValueBase> values) => _ = SendMonitoringDataAsync(values);

        private void DataQueueFileReceiving(FileSensorValue value) => _ = DataQueueFileReceivingAsync(value);


        private async Task SendMonitoringDataAsync(List<SensorValueBase> values)
        {
            try
            {
                if (values.Count == 0)
                    return;

                string jsonString = JsonConvert.SerializeObject(values.Cast<object>());

                if (_logManager.WriteDebug)
                    _logManager.Logger?.Debug($"{nameof(SendMonitoringDataAsync)}: {jsonString}");

                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync(_listSendingAddress, data);

                if (!res.IsSuccessStatusCode)
                    _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                _dataQueue.PushFailValues(values);
                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }

        private async Task DataQueueFileReceivingAsync(FileSensorValue value)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(value);

                if (_logManager.WriteDebug)
                    _logManager.Logger?.Debug($"{nameof(DataQueueFileReceivingAsync)}: {jsonString}");

                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync(_fileSendingAddress, data);

                if (!res.IsSuccessStatusCode)
                    _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                _dataQueue.PushFailValue(value);
                _logManager.Logger?.Error($"Failed to send: {e}");
            }
        }
    }
}