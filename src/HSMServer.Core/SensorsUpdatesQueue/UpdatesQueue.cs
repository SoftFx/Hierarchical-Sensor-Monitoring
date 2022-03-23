using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private readonly ConcurrentQueue<SensorValueBase> _queue;

        private bool _run;

        public event Action<SensorValueBase> NewItemEvent;


        public UpdatesQueue()
        {
            _queue = new ConcurrentQueue<SensorValueBase>();
            _run = true;

            ThreadPool.QueueUserWorkItem(_ => RunManageThread());
        }


        public void AddItem(SensorValueBase sensorValue) =>
            _queue.Enqueue(sensorValue);

        public void Stop() => _run = false;


        private async void RunManageThread()
        {
            while (_run)
            {
                if (_queue.TryDequeue(out var data))
                    NewItemEvent?.Invoke(data);
                else
                    await Task.Delay(10);
            }
        }
    }
}
