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
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    internal sealed class HSMClient : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly LoggerManager _logManager;
        private readonly IDataQueue _dataQueue;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;


        internal HSMClient(CollectorOptions options, IDataQueue dataQueue, LoggerManager logger)
        {
            _dataQueue = dataQueue;
            _logManager = logger;

            _endpoints = new Endpoints(options);

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);

            _dataQueue.SendValueHandler += RecieveQueueData;
            _dataQueue.SendValuesHandler += RecieveQueueData;
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

            await SendData(value);
        }


        public void Dispose()
        {
            _tokenSource.Cancel();

            _dataQueue.SendValueHandler -= RecieveQueueData;
            _dataQueue.SendValuesHandler -= RecieveQueueData;

            _client.Dispose();
        }


        internal Task SendData(List<SensorValueBase> values)
        {
            try
            {
                return RequestToServer(values.Cast<object>(), _endpoints.List);
            }
            catch (Exception ex)
            {
                _logManager.Logger?.Error($"Failed to send: {ex}");

                foreach (var value in values)
                    _dataQueue.PushFailValue(value);
            }

            return Task.CompletedTask;
        }

        internal Task SendData(SensorValueBase value)
        {
            try
            {
                switch (value)
                {
                    case BoolSensorValue boolV:
                        return RequestToServer(boolV, _endpoints.Bool);
                    case IntSensorValue intV:
                        return RequestToServer(intV, _endpoints.Integer);
                    case DoubleSensorValue doubleV:
                        return RequestToServer(doubleV, _endpoints.Double);
                    case StringSensorValue stringV:
                        return RequestToServer(stringV, _endpoints.String);
                    case TimeSpanSensorValue timeSpanV:
                        return RequestToServer(timeSpanV, _endpoints.Timespan);
                    case IntBarSensorValue intBarV:
                        return RequestToServer(intBarV, _endpoints.IntBar);
                    case DoubleBarSensorValue doubleBarV:
                        return RequestToServer(doubleBarV, _endpoints.DoubleBar);
                    case FileSensorValue fileV:
                        return RequestToServer(fileV, _endpoints.File);
                }
            }
            catch (Exception ex)
            {
                _logManager.Logger?.Error($"Failed to send: {ex}");

                _dataQueue.PushFailValue(value);
            }

            return Task.CompletedTask;
        }


        private void RecieveQueueData(SensorValueBase value) => SendData(value);

        private void RecieveQueueData(List<SensorValueBase> value) => SendData(value);


        private async Task RequestToServer<T>(T value, string uri) where T : class
        {
            string json = JsonConvert.SerializeObject(value);

            if (_logManager.WriteDebug)
                _logManager.Logger?.Debug($"{nameof(RequestToServer)}: {json}");

            var data = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await _client.PostAsync(uri, data, _tokenSource.Token);

            if (!res.IsSuccessStatusCode)
                _logManager.Logger?.Error($"Failed to send data. StatusCode={res.StatusCode}");
        }
    }
}