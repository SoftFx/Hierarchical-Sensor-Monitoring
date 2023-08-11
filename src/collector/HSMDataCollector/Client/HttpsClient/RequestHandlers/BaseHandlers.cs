using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient
{
    internal abstract class BaseHandlers<T> : IDisposable
    {
        protected readonly ICollectorLogger _logger;
        protected readonly ISyncQueue<T> _queue;
        protected readonly Endpoints _endpoints;

        internal event Func<object, string, Task<HttpResponseMessage>> InvokeRequest;


        protected BaseHandlers(ISyncQueue<T> queue, Endpoints endpoints, ICollectorLogger logger)
        {
            _endpoints = endpoints;
            _logger = logger;
            _queue = queue;

            _queue.NewValuesEvent += RecieveDataQueue;
            _queue.NewValueEvent += RecieveDataQueue;
        }


        internal abstract Task SendRequest(List<T> values);

        internal abstract Task SendRequest(T value);


        protected virtual Task<HttpResponseMessage> RequestToServer(object value, string uri)
        {
            try
            {
                return InvokeRequest(value, uri);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to send data. Error={ex} Data={value}");

                if (value is IEnumerable<T> list)
                    foreach (var data in list)
                        _queue.PushFailValue(data);
                else if (value is T single)
                    _queue.PushFailValue(single);

                return Task.FromResult<HttpResponseMessage>(null);
            }
        }


        private void RecieveDataQueue(List<T> value) => SendRequest(value);

        private void RecieveDataQueue(T value) => SendRequest(value);


        public void Dispose()
        {
            _queue.NewValuesEvent -= RecieveDataQueue;
            _queue.NewValueEvent -= RecieveDataQueue;
        }
    }
}