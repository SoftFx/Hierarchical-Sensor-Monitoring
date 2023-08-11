using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
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

namespace HSMDataCollector.Client
{
    internal sealed class HsmHttpsClient : IDisposable
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private readonly ICollectorLogger _logger;
        private readonly IQueueManager _queueManager;
        private readonly Endpoints _endpoints;
        private readonly HttpClient _client;


        private ISyncQueue<SensorValueBase> DataQueue => _queueManager.Data;

        private ICommandQueue CommandsQueue => _queueManager.Commands;



        internal HsmHttpsClient(CollectorOptions options, IQueueManager queueManager, ICollectorLogger logger)
        {
            _queueManager = queueManager;
            _logger = logger;

            _endpoints = new Endpoints(options);

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, error) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            _client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            });

            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), options.AccessKey);

            DataQueue.NewValueEvent += RecieveDataQueue;
            DataQueue.NewValuesEvent += RecieveDataQueue;
        }


        internal async Task SendFileAsync(FileInfo fileInfo, string sensorPath, SensorStatus sensorStatus = SensorStatus.Ok, string comment = "")
        {
            async Task<List<byte>> GetFileBytes()
            {
                try
                {
                    using (var file = File.Open(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var stream = new StreamReader(file))
                            return Encoding.UTF8.GetBytes(await stream.ReadToEndAsync()).ToList();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex.Message);
                    throw;
                }
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

            DataQueue.NewValueEvent -= RecieveDataQueue;
            DataQueue.NewValuesEvent -= RecieveDataQueue;

            _client.Dispose();
        }


        internal async Task<ConnectionResult> TestConnection()
        {
            try
            {
                var connect = await _client.GetAsync(_endpoints.TestConnection, _tokenSource.Token);

                return connect.IsSuccessStatusCode
                    ? ConnectionResult.Ok
                    : new ConnectionResult(connect.StatusCode, $"{connect.ReasonPhrase} ({await connect.Content.ReadAsStringAsync()})");
            }
            catch (Exception ex)
            {
                return new ConnectionResult(null, ex.Message);
            }
        }

        internal Task SendData(List<SensorValueBase> values) => values.Count > 0
            ? RequestToServer(values.ToList().Cast<object>(), _endpoints.List)
            : Task.CompletedTask;

        internal Task SendData(SensorValueBase value)
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
                case VersionSensorValue versionV:
                    return RequestToServer(versionV, _endpoints.Version);
                default:
                    _logger.Error($"Unsupported sensor type: {value.Path}");
                    return Task.CompletedTask;
            }
        }

        private void RecieveDataQueue(SensorValueBase value) => SendData(value);

        private void RecieveDataQueue(List<SensorValueBase> value) => SendData(value);

        private async Task RequestToServer<T>(T value, string uri) where T : class
        {
            try
            {
                string json = JsonConvert.SerializeObject(value);

                _logger.Debug($"{nameof(RequestToServer)}: {json}");

                var data = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _client.PostAsync(uri, data, _tokenSource.Token);

                if (!res.IsSuccessStatusCode)
                    _logger.Error($"Failed to send data. StatusCode={res.StatusCode}. Data={json}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to send: {ex}");

                if (value is IEnumerable<SensorValueBase> list)
                    foreach (var data in list)
                        DataQueue.PushFailValue(data);
                else if (value is SensorValueBase single)
                    DataQueue.PushFailValue(single);
            }
        }
    }
}