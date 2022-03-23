using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int Delay = 10;
        private const int PackageMaxSize = 100;

        private readonly ConcurrentQueue<SensorValueBase> _queue;

        private bool _run;

        public event Action<IEnumerable<SensorValueBase>> NewItemsEvent;


        public UpdatesQueue()
        {
            _queue = new ConcurrentQueue<SensorValueBase>();
            _run = true;

            ThreadPool.QueueUserWorkItem(_ => RunManageThread());
        }


        public void AddItem(SensorValueBase sensorValue) =>
            _queue.Enqueue(sensorValue);

        public void AddItems(IEnumerable<SensorValueBase> sensorValues)
        {
            foreach (var value in sensorValues)
                AddItem(value);
        }

        public void Dispose() => _run = false;


        private async void RunManageThread()
        {
            while (_run)
            {
                var data = GetDataPackage();

                if (data.Count > 0)
                    NewItemsEvent?.Invoke(data);
                else
                    await Task.Delay(Delay);
            }
        }

        private List<SensorValueBase> GetDataPackage()
        {
            var data = new List<SensorValueBase>(PackageMaxSize);

            for (int i = 0; i < PackageMaxSize; ++i)
            {
                if (!_queue.TryDequeue(out var value))
                    break;

                data.Add(value);
            }

            return data;
        }
    }
}
