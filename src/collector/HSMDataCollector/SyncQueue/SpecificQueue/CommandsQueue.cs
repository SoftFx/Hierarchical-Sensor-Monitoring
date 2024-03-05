using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Requests;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    internal class CommandsQueue : SyncQueue<PriorityRequest>, ICommandQueue
    {
        private readonly ConcurrentDictionary<(Guid, string), TaskCompletionSource<bool>> _requestStorage = new ConcurrentDictionary<(Guid, string), TaskCompletionSource<bool>>();
        private readonly ILoggerManager _logger;

        protected override string QueueName => "Commands";


        public CommandsQueue(CollectorOptions options, ILoggerManager logger) : base(options, TimeSpan.FromSeconds(5))
        {
            _logger = logger;
        }


        public Task<bool> WaitServerResponse(PriorityRequest request)
        {
            if (_requestStorage.TryRemove(request.Key, out var source))
            {
                source.TrySetCanceled();
                _logger.Error($"Command request by key {request.Key} has been canceled");
            }

            source = new TaskCompletionSource<bool>();

            if (_requestStorage.TryAdd(request.Key, source))
            {
                Add(request);
                _logger.Info($"Command request by key {request.Key} has been added");
            }

            return source.Task;
        }

        public void SetResult((Guid, string) key, bool result)
        {
            if (_requestStorage.TryGetValue(key, out var source))
                source.SetResult(result);
        }

        public void SetCancel((Guid, string) key)
        {
            if (_requestStorage.TryGetValue(key, out var source))
                source.SetCanceled();
        }
    }
}