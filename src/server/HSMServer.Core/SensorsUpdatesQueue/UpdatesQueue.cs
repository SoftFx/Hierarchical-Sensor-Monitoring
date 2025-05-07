using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int PackageMaxSize = 1000;
        private readonly ManualResetEventSlim _event = new ManualResetEventSlim();

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentQueue<StoreInfo> _queue = new ConcurrentQueue<StoreInfo>();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly Task _task;

        public event Action<IEnumerable<StoreInfo>> ItemsAdded;


        public UpdatesQueue()
        {
            _task = Task.Run(() =>
            {
                var token = _cts.Token;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        _event.Wait(token);
                        _event.Reset();

                        while (!_queue.IsEmpty && !token.IsCancellationRequested)
                            ItemsAdded?.Invoke(GetDataPackage());
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                    }
                }
            });
        }


        public void AddItem(StoreInfo storeInfo)
        {
            _queue.Enqueue(storeInfo);
            _event.Set();
        }

        public void AddItems(IEnumerable<StoreInfo> storeInfos)
        {
            foreach (var store in storeInfos)
                AddItem(store);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _task.Wait();
            _cts.Dispose();
            _event.Dispose();
            _task.Dispose();
        }


        private IEnumerable<StoreInfo> GetDataPackage()
        {
            for (int i = 0; i < PackageMaxSize; ++i)
            {
                if (!_queue.TryDequeue(out var value))
                    break;

                yield return value;
            }
        }
    }
}
