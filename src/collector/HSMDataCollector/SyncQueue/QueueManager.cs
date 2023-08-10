using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue
{
    internal sealed class QueueManager : IQueueManager
    {
        private readonly List<SyncQueue> _queueList = new List<SyncQueue>();


        public ISyncQueue<SensorValueBase> Data { get; }

        public ICommandQueue Commands { get; }


        internal QueueManager(CollectorOptions options) : base()
        {
            Commands = RegisterQueue(new CommandsQueue(options));
            Data = RegisterQueue(new SensorDataQueue(options));
        }


        private T RegisterQueue<T>(T queue) where T : SyncQueue
        {
            _queueList.Add(queue);

            return queue;
        }


        public void Init() => _queueList.ForEach(q => q.Init());

        public void Stop() => _queueList.ForEach(q => q.Stop());

        public void Dispose() => _queueList.ForEach(q => q.Dispose());
    }
}